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

// --ensure-schema creates the database and containers before seeding. It is passed only
// by the local Aspire bootstrap: the emulator starts empty and nothing else provisions
// its schema. In the cloud the flag is omitted because Terraform owns schema and the
// seeder's identity holds only a data-plane role.
var ensureSchema = args.Contains("--ensure-schema", StringComparer.OrdinalIgnoreCase);
var positionalArgs = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();

var databaseName = configuration["CosmosDatabase"] ?? "invoicemanager";

var seedFilePath = positionalArgs.Length > 0
    ? positionalArgs[0]
    : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "seed", "invoice-configurations.json");

Console.WriteLine($"Seeder starting.");
Console.WriteLine($"  Database:      {databaseName}");
Console.WriteLine($"  Ensure schema: {ensureSchema}");
Console.WriteLine($"  Seed file:     {Path.GetFullPath(seedFilePath)}");

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
    new Money(r.DefaultExpectedAmount, r.DefaultExpectedCurrency),
    Enum.Parse<VatMode>(r.DefaultVatMode, ignoreCase: true),
    r.IsActive,
    r.OneDriveDestination,
    DateOnly.ParseExact(r.StartDate, "O", CultureInfo.InvariantCulture),
    r.BillingAccountId,
    r.DateToleranceDays,
    r.AmountTolerance)).ToList();

var cosmosClient = CosmosClientFactory.Create(configuration);

try
{
    if (ensureSchema)
    {
        await EnsureSchemaAsync(cosmosClient, databaseName);
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
            "See tools/dev-setup/Set-SeedEnvironment.ps1.example for a setup script.");
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
