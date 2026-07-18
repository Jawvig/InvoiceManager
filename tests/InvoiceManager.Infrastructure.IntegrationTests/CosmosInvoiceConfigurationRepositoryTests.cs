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
        await emulator.EnsureDatabaseAndContainerAsync(
            TestDatabase, new ContainerProperties("invoice-configurations", "/integrationType"));

        repository = new CosmosInvoiceConfigurationRepository(emulator.Client, TestDatabase);
    }

    public async Task DisposeAsync()
    {
        await emulator.DeleteDatabaseAsync(TestDatabase);
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

    [Fact]
    public async Task CreateAndReplace_AppendRevisions_AndExcludeThemFromLiveQueries()
    {
        var actor = new InvoiceConfigurationActor("actor-1", "Admin User");
        var draft = BuildConfiguration(new("audited-config"), isActive: false);
        var created = await repository!.CreateAsync(draft, actor);

        var updated = draft with { InvoiceDescription = "Updated" };
        await repository.ReplaceAsync(
            updated, created.ETag, InvoiceConfigurationRevisionAction.Updated, actor);

        var live = await repository.ListAllAsync();
        var revisions = await repository.ListRevisionsAsync(draft.Id, draft.IntegrationType);
        Assert.Single(live, x => x.Configuration.Id == draft.Id);
        Assert.Collection(
            revisions,
            x => Assert.Equal(InvoiceConfigurationRevisionAction.Created, x.Action),
            x => Assert.Equal(InvoiceConfigurationRevisionAction.Updated, x.Action));
        Assert.Equal("Updated", revisions[1].Snapshot.InvoiceDescription);
    }

    [Fact]
    public async Task FirstMutationOfSeededConfiguration_AppendsPreAuditBaseline()
    {
        var configuration = BuildConfiguration(new("legacy-audit"));
        await repository!.CreateIfNotExistsAsync(configuration);
        var stored = await repository.GetAsync(configuration.Id, configuration.IntegrationType) switch
        {
            StoredInvoiceConfiguration value => value,
            _ => throw new Xunit.Sdk.XunitException("Expected the seeded configuration."),
        };

        await repository.ReplaceAsync(
            configuration with { IsActive = false }, stored.ETag,
            InvoiceConfigurationRevisionAction.Deactivated,
            new("actor-1", "Admin User"));

        var revisions = await repository.ListRevisionsAsync(configuration.Id, configuration.IntegrationType);
        Assert.Equal(InvoiceConfigurationRevisionAction.PreAuditBaseline, revisions[0].Action);
        Assert.Null(revisions[0].ActorObjectId);
        Assert.Equal(InvoiceConfigurationRevisionAction.Deactivated, revisions[1].Action);
    }

    [Fact]
    public async Task Replace_RejectsStaleEtag()
    {
        var configuration = BuildConfiguration(new("etag-conflict"), isActive: false);
        var stored = await repository!.CreateAsync(configuration, new("actor", "Admin"));
        await repository.ReplaceAsync(
            configuration with { InvoiceDescription = "First" }, stored.ETag,
            InvoiceConfigurationRevisionAction.Updated, new("actor", "Admin"));

        await Assert.ThrowsAsync<InvoiceConfigurationConflictException>(() => repository.ReplaceAsync(
            configuration with { InvoiceDescription = "Stale" }, stored.ETag,
            InvoiceConfigurationRevisionAction.Updated, new("actor", "Admin")));
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
            new AmountMatchingCriteria(new Money(10.00m, "GBP"), 0m),
            VatMode.Exclusive,
            IsActive: isActive,
            OneDriveDestination: "/drives/test/root:/Bills/Test",
            StartDate: new DateOnly(2025, 1, 1),
            BillingAccountId: "test:billing:account",
            DateToleranceDays: 5);
}
