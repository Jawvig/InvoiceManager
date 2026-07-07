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
