using InvoiceManager.Core;
using InvoiceManager.Infrastructure.CosmosDb;
using Microsoft.Azure.Cosmos;
using NodaMoney;

namespace InvoiceManager.Infrastructure.IntegrationTests;

/// <summary>
/// Integration tests for <see cref="CosmosInvoiceConfigurationRepository"/>.
/// Requires the Azure Cosmos DB Emulator running at https://localhost:8081.
/// Run with: dotnet test --filter "Category=Integration"
/// Start the emulator: start the "Azure Cosmos DB Emulator" from the Start menu,
/// or run: &amp; "C:\Program Files\Azure Cosmos DB Emulator\CosmosDB.Emulator.exe"
/// </summary>
[Trait("Category", "Integration")]
public sealed class CosmosInvoiceConfigurationRepositoryTests : IAsyncLifetime
{
    // The Cosmos DB Emulator's well-known endpoint and key.
    private const string EmulatorEndpoint = "https://localhost:8081";
    private const string EmulatorKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
    private const string TestDatabase = "invoicemanager-integration-tests";

    private CosmosClient? cosmosClient;
    private CosmosInvoiceConfigurationRepository? repository;

    public async Task InitializeAsync()
    {
        var endpoint = Environment.GetEnvironmentVariable("COSMOS_INTEGRATION_ENDPOINT") ?? EmulatorEndpoint;
        var key = Environment.GetEnvironmentVariable("COSMOS_INTEGRATION_KEY") ?? EmulatorKey;

        var options = CosmosInvoiceConfigurationRepository.BuildClientOptions();
        // Allow self-signed cert from the local emulator.
        options.HttpClientFactory = () => new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        });
        options.ConnectionMode = ConnectionMode.Gateway;
        cosmosClient = new CosmosClient(endpoint, key, options);

        var db = await cosmosClient.CreateDatabaseIfNotExistsAsync(TestDatabase);
        await db.Database.CreateContainerIfNotExistsAsync(
            new ContainerProperties("invoice-configurations", "/integrationType"));

        repository = new CosmosInvoiceConfigurationRepository(cosmosClient, TestDatabase);
    }

    public async Task DisposeAsync()
    {
        if (cosmosClient is not null)
        {
            try
            {
                await cosmosClient.GetDatabase(TestDatabase).DeleteAsync();
            }
            finally
            {
                cosmosClient.Dispose();
            }
        }
    }

    [Fact]
    public async Task CreateIfNotExistsAsync_InsertsConfiguration_WhenNotPresent()
    {
        var config = BuildConfiguration(new InvoiceConfigurationId("create-test"));

        await repository!.CreateIfNotExistsAsync(config);

        var all = await repository.ListActiveAsync();
        Assert.Contains(all, c => c.Id == new InvoiceConfigurationId("create-test"));
    }

    [Fact]
    public async Task CreateIfNotExistsAsync_DoesNotOverwrite_WhenAlreadyPresent()
    {
        var original = BuildConfiguration(
            new InvoiceConfigurationId("idempotency-test"),
            invoiceDescription: "Original");
        await repository!.CreateIfNotExistsAsync(original);

        var modified = original with { InvoiceDescription = "Modified" };
        await repository.CreateIfNotExistsAsync(modified);

        var all = await repository.ListActiveAsync();
        var stored = Assert.Single(all, c => c.Id == new InvoiceConfigurationId("idempotency-test"));
        Assert.Equal("Original", stored.InvoiceDescription);
    }

    [Fact]
    public async Task ListActiveAsync_ReturnsOnlyActiveConfigurations()
    {
        var active = BuildConfiguration(new InvoiceConfigurationId("list-active"), isActive: true);
        var inactive = BuildConfiguration(new InvoiceConfigurationId("list-inactive"), isActive: false);
        await repository!.CreateIfNotExistsAsync(active);
        await repository.CreateIfNotExistsAsync(inactive);

        var results = await repository.ListActiveAsync();

        Assert.Contains(results, c => c.Id == new InvoiceConfigurationId("list-active"));
        Assert.DoesNotContain(results, c => c.Id == new InvoiceConfigurationId("list-inactive"));
    }

    private static InvoiceConfiguration BuildConfiguration(
        InvoiceConfigurationId id,
        string invoiceDescription = "Test Invoice",
        bool isActive = true) =>
        new(
            id,
            IntegrationType.Microsoft365,
            invoiceDescription,
            InvoiceFrequency.Monthly,
            new Money(10.00m, "GBP"),
            VatMode.Exclusive,
            IsActive: isActive,
            OneDriveDestination: "/drives/test/root:/Bills/Test",
            StartDate: new DateOnly(2025, 1, 1),
            BillingAccountId: "test:billing:account",
            DateToleranceDays: 5);
}
