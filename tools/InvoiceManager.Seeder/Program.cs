using System.Text.Json;
using Azure.Identity;
using InvoiceManager.Core;
using InvoiceManager.Infrastructure.CosmosDb;
using InvoiceManager.Seeder;
using Microsoft.Azure.Cosmos;
using NodaMoney;

var endpoint = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT")
    ?? throw new InvalidOperationException(
        "COSMOS_ENDPOINT environment variable is required. " +
        "Set it to the Cosmos DB account endpoint URI.");

var databaseName = Environment.GetEnvironmentVariable("COSMOS_DATABASE") ?? "invoicemanager";

var seedFilePath = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "seed", "invoice-configurations.json");

Console.WriteLine($"Seeder starting.");
Console.WriteLine($"  Cosmos endpoint: {endpoint}");
Console.WriteLine($"  Database:        {databaseName}");
Console.WriteLine($"  Seed file:       {Path.GetFullPath(seedFilePath)}");

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
    DateOnly.ParseExact(r.StartDate, "yyyy-MM-dd"),
    r.BillingAccountId,
    r.DateToleranceDays)).ToList();

var cosmosClient = new CosmosClient(
    accountEndpoint: endpoint,
    tokenCredential: new DefaultAzureCredential(),
    clientOptions: CosmosInvoiceConfigurationRepository.BuildClientOptions());

var repository = new CosmosInvoiceConfigurationRepository(cosmosClient, databaseName);
var seeder = new ConfigurationSeeder(repository);

try
{
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
