using System.Globalization;
using System.Text.Json;
using InvoiceManager.Core;
using InvoiceManager.Infrastructure.CosmosDb;
using InvoiceManager.Seeder;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using NodaMoney;

// Cosmos connection details come from configuration so the seeder shares a single
// client-creation path with the running app (CosmosClientFactory): locally the Aspire
// emulator supplies ConnectionStrings:cosmos (key-based); in the cloud the deploy script
// supplies CosmosEndpoint and the seeder authenticates with DefaultAzureCredential.
var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .Build();

// Flags:
//   --ensure-schema   create the database and containers before seeding. Passed only by the
//                     local Aspire bootstrap: the emulator starts empty. In the cloud
//                     Terraform owns schema and the seeder's identity holds only a data role.
//   --clear-database  delete every item from the containers (data-plane deletes) before
//                     seeding, giving a clean slate. Refused against production without --force.
//   --force           override the production --clear-database guard.
//   --environment <n> deployment environment (required). When "test", downloads are nested
//                     under a root "Test" folder so they never collide with production files.
var (ensureSchema, clearDatabase, force, environment, positionalArgs) = ParseArgs(args);

if (string.IsNullOrWhiteSpace(environment))
{
    await Console.Error.WriteLineAsync(
        "The --environment option is required, e.g. --environment test or --environment production.");
    Environment.Exit(5);
}

var isTest = string.Equals(environment, "test", StringComparison.OrdinalIgnoreCase);
var isProduction = string.Equals(environment, "production", StringComparison.OrdinalIgnoreCase);

if (clearDatabase && isProduction && !force)
{
    await Console.Error.WriteLineAsync(
        "Refusing --clear-database against the production environment. Pass --force to override.");
    Environment.Exit(4);
}

var databaseName = configuration["CosmosDatabase"] ?? "invoicemanager";

var seedFilePath = positionalArgs.Length > 0
    ? positionalArgs[0]
    : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "seed", "invoice-configurations.json");

Console.WriteLine($"Seeder starting.");
Console.WriteLine($"  Database:       {databaseName}");
Console.WriteLine($"  Environment:    {environment}");
Console.WriteLine($"  Ensure schema:  {ensureSchema}");
Console.WriteLine($"  Clear database: {clearDatabase}");
Console.WriteLine($"  Seed file:      {Path.GetFullPath(seedFilePath)}");

var json = await File.ReadAllTextAsync(seedFilePath);

// The seed file ships readable placeholders for values we must not commit (the
// OneDrive drive id lives inside a path, the M365 billing account id is a whole
// field). Substitute the real values from configuration before deserializing so a
// single string replace covers both the embedded and standalone cases.
json = ReplaceSeedTokens(json, configuration);

var records = JsonSerializer.Deserialize<List<SeedInvoiceConfigurationRecord>>(
    json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
    ?? throw new InvalidOperationException("Seed file is empty or invalid.");

Console.WriteLine($"Loaded {records.Count} configuration(s) from seed file.");

var configurations = records.Select(r => new InvoiceConfiguration(
    new InvoiceConfigurationId(r.Id),
    Enum.Parse<IntegrationType>(r.IntegrationType, ignoreCase: true),
    r.InvoiceDescription,
    Enum.Parse<InvoiceFrequency>(r.Frequency, ignoreCase: true),
    r.AmountMatchingCriteria is { } amountCriteria
        ? new AmountMatchingCriteria(new Money(amountCriteria.Amount, amountCriteria.Currency), amountCriteria.AmountTolerance)
        : Option.None,
    Enum.Parse<VatMode>(r.DefaultVatMode, ignoreCase: true),
    r.IsActive,
    InjectEnvironmentFolder(r.OneDriveDestination, isTest),
    DateOnly.ParseExact(r.StartDate, "O", CultureInfo.InvariantCulture),
    r.BillingAccountId,
    r.DateToleranceDays)).ToList();

var cosmosClient = CosmosClientFactory.Create(configuration);

try
{
    if (ensureSchema)
    {
        await EnsureSchemaAsync(cosmosClient, databaseName);
    }

    if (clearDatabase)
    {
        await ClearDatabaseAsync(cosmosClient, databaseName);
    }

    var repository = new CosmosInvoiceConfigurationRepository(cosmosClient, databaseName);
    var seeder = new ConfigurationSeeder(repository);
    await seeder.SeedAsync(configurations);
}
catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
{
    await Console.Error.WriteLineAsync(
        $"Cosmos DB returned 403 Forbidden — the RBAC role assignment may not yet have " +
        $"propagated. ({ex.Message})");
    Environment.Exit(2);
}

Console.WriteLine("Seeding complete.");

static string ReplaceSeedTokens(string json, IConfiguration configuration)
{
    // token in seed file  ->  config key (env var: InvoiceManager__Seed__<name>)
    var tokens = new (string Token, string ConfigKey, string EnvVar)[]
    {
        ("REPLACE_WITH_DRIVE_ID", "InvoiceManager:Seed:DriveId", "InvoiceManager__Seed__DriveId"),
        ("REPLACE_WITH_BILLING_ACCOUNT_ID", "InvoiceManager:Seed:BillingAccountId", "InvoiceManager__Seed__BillingAccountId"),
        ("REPLACE_WITH_AZURE_BILLING_ACCOUNT_ID", "InvoiceManager:Seed:AzureBillingAccountId", "InvoiceManager__Seed__AzureBillingAccountId"),
    };

    var missing = new List<string>();
    foreach (var (token, configKey, envVar) in tokens)
    {
        // Only require a value when its token is actually present, so seed files that
        // don't use a token (e.g. future non-M365 configs) need no extra configuration.
        if (!json.Contains(token, StringComparison.Ordinal))
        {
            continue;
        }

        var value = configuration[configKey];
        if (string.IsNullOrWhiteSpace(value))
        {
            missing.Add(envVar);
            continue;
        }

        json = json.Replace(token, value, StringComparison.Ordinal);
    }

    if (missing.Count > 0)
    {
        Console.Error.WriteLine(
            "Seed file requires real values that are not configured. Set the following " +
            "environment variable(s) before seeding:");
        foreach (var envVar in missing)
        {
            Console.Error.WriteLine($"  {envVar}");
        }
        Console.Error.WriteLine(
            "Run tools/dev-setup/Set-SeedEnvironment.ps1 to discover and set the required values.");
        Environment.Exit(3);
    }

    return json;
}

static async Task EnsureSchemaAsync(CosmosClient client, string databaseName)
{
    var database = await client.CreateDatabaseIfNotExistsAsync(databaseName);
    Console.WriteLine($"Ensured database '{databaseName}'.");

    foreach (var container in CosmosSchema.Containers)
    {
        await database.Database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(container.Name, container.PartitionKeyPath));
        Console.WriteLine($"Ensured container '{container.Name}' ({container.PartitionKeyPath}).");
    }
}

// Deletes every item from each known container using data-plane deletes only, so it works
// with just the seeder's Cosmos data-plane role (no control-plane container recreate).
static async Task ClearDatabaseAsync(CosmosClient client, string databaseName)
{
    foreach (var definition in CosmosSchema.Containers)
    {
        var container = client.GetContainer(databaseName, definition.Name);
        var partitionKeyProperty = definition.PartitionKeyPath.TrimStart('/');

        // Read all ids + partition-key values first, then delete, so we never mutate a
        // container while its query iterator is still open.
        var toDelete = new List<(string Id, string PartitionKey)>();
        using var iterator = container.GetItemQueryIterator<JsonElement>("SELECT * FROM c");
        while (iterator.HasMoreResults)
        {
            foreach (var item in await iterator.ReadNextAsync())
            {
                var id = item.GetProperty("id").GetString();
                var partitionKey = item.GetProperty(partitionKeyProperty).GetString();
                if (id is not null && partitionKey is not null)
                {
                    toDelete.Add((id, partitionKey));
                }
            }
        }

        foreach (var (id, partitionKey) in toDelete)
        {
            await container.DeleteItemAsync<JsonElement>(id, new PartitionKey(partitionKey));
        }

        Console.WriteLine($"Cleared {toDelete.Count} item(s) from '{definition.Name}'.");
    }
}

// For the test environment, nest every OneDrive destination under a single root "Test"
// folder (inserted immediately after the drive "root:/" marker), mirroring the production
// tree inside it so test downloads never collide with production files.
static string InjectEnvironmentFolder(string oneDriveDestination, bool isTest)
{
    if (!isTest)
    {
        return oneDriveDestination;
    }

    const string rootMarker = "root:/";
    var markerIndex = oneDriveDestination.IndexOf(rootMarker, StringComparison.Ordinal);
    if (markerIndex < 0)
    {
        // Unrecognised path shape; leave it untouched rather than corrupt it.
        return oneDriveDestination;
    }

    var insertionPoint = markerIndex + rootMarker.Length;
    return string.Concat(
        oneDriveDestination.AsSpan(0, insertionPoint),
        "Test/",
        oneDriveDestination.AsSpan(insertionPoint));
}

static (bool EnsureSchema, bool ClearDatabase, bool Force, string? Environment, string[] Positional) ParseArgs(string[] args)
{
    var ensureSchema = false;
    var clearDatabase = false;
    var force = false;
    string? environment = null;
    var positional = new List<string>();

    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        if (arg.Equals("--ensure-schema", StringComparison.OrdinalIgnoreCase))
        {
            ensureSchema = true;
        }
        else if (arg.Equals("--clear-database", StringComparison.OrdinalIgnoreCase))
        {
            clearDatabase = true;
        }
        else if (arg.Equals("--force", StringComparison.OrdinalIgnoreCase))
        {
            force = true;
        }
        else if (arg.Equals("--environment", StringComparison.OrdinalIgnoreCase))
        {
            if (i + 1 >= args.Length)
            {
                throw new ArgumentException("--environment requires a value, e.g. --environment test.");
            }

            environment = args[++i];
        }
        else if (arg.StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Unknown option '{arg}'.");
        }
        else
        {
            positional.Add(arg);
        }
    }

    return (ensureSchema, clearDatabase, force, environment, positional.ToArray());
}
