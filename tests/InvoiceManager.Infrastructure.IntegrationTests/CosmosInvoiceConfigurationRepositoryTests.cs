using InvoiceManager.Core;
using InvoiceManager.Infrastructure.CosmosDb;
using Microsoft.Azure.Cosmos;
using NodaMoney;

namespace InvoiceManager.Infrastructure.IntegrationTests;

[Collection("CosmosIntegration")]
[Trait("Category", "Integration")]
public sealed class CosmosInvoiceConfigurationRepositoryTests : IAsyncLifetime
{
    private const string TestDatabase = "invoicemanager-integration-tests";

    private readonly CosmosEmulatorFixture emulator;
    private CosmosInvoiceConfigurationRepository? repository;

    public CosmosInvoiceConfigurationRepositoryTests(CosmosEmulatorFixture emulator)
    {
        this.emulator = emulator;
    }

    public async Task InitializeAsync()
    {
        var db = await emulator.Client.CreateDatabaseIfNotExistsAsync(TestDatabase);
        await db.Database.CreateContainerIfNotExistsAsync(
            new ContainerProperties("invoice-configurations", "/integrationType"));

        repository = new CosmosInvoiceConfigurationRepository(emulator.Client, TestDatabase);
    }

    public async Task DisposeAsync()
    {
        await emulator.Client.GetDatabase(TestDatabase).DeleteAsync();
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
            DateToleranceDays: 5,
            AmountTolerance: 0m);
}
