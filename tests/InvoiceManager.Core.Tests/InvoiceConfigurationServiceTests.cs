using InvoiceManager.Core;
using InvoiceManager.TestSupport;

namespace InvoiceManager.Core.Tests;

public sealed class InvoiceConfigurationServiceTests
{
    private static readonly InvoiceConfigurationActor Actor = new("actor-id", "Admin User");

    [Fact]
    public async Task Create_RequiresInactiveDraft()
    {
        var service = new InvoiceConfigurationService(new FakeConfigurationRepository());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(Configurations.Build(isActive: true), Actor));
    }

    [Fact]
    public async Task Create_RejectsDuplicateIdAcrossIntegrationTypes()
    {
        var existing = Configurations.Build();
        var service = new InvoiceConfigurationService(new FakeConfigurationRepository(existing));
        var duplicate = Configurations.Build(
            isActive: false,
            integrationConfiguration: new GraphEmailIntegrationConfiguration("sender@example.com", "Invoice")) with
        {
            Id = existing.Id,
        };

        var result = await service.CreateAsync(duplicate, Actor);

        Assert.True(result is DuplicateInvoiceConfigurationId duplicateId && duplicateId.Id == existing.Id);
    }

    [Fact]
    public async Task Create_RejectsSameSearchCriteria_EvenWithDifferentFolderAndId()
    {
        var existing = Configurations.Build(
            id: new("existing-config"),
            integrationConfiguration: new MicrosoftBillingIntegrationConfiguration("account-1"));
        var service = new InvoiceConfigurationService(new FakeConfigurationRepository(existing));
        var duplicate = Configurations.Build(
            id: new("new-config"),
            isActive: false,
            integrationConfiguration: new MicrosoftBillingIntegrationConfiguration("account-1"),
            oneDriveFolder: new OneDriveFolder("drive-2", "Drive Two", "folder-2", "/Bills/Other"));

        var result = await service.CreateAsync(duplicate, Actor);

        Assert.True(result is DuplicateInvoiceConfigurationSearchCriteria conflict && conflict.ConflictingId == existing.Id);
    }

    [Fact]
    public async Task Update_ReturnsConflict_WhenEtagIsStale()
    {
        // Exercises the repository-level translation added for issue #93: the fake repository
        // (mirroring CosmosInvoiceConfigurationRepository) returns InvoiceConfigurationConflict
        // itself when the etag is stale, rather than throwing - the service must pass that case
        // through its exhaustive switch unchanged.
        var original = Configurations.Build(isActive: false);
        var repository = new FakeConfigurationRepository(original);
        var service = new InvoiceConfigurationService(repository);
        var updated = original with { InvoiceDescription = "Updated description" };

        var result = await service.UpdateAsync(original, updated, "stale-etag", Actor);

        Assert.True(result is InvoiceConfigurationConflict);
    }

    [Fact]
    public async Task SetActive_ReturnsConflict_WhenEtagIsStale()
    {
        var original = Configurations.Build(isActive: false);
        var repository = new FakeConfigurationRepository(original);
        var service = new InvoiceConfigurationService(repository);
        var stored = new StoredInvoiceConfiguration(original, "stale-etag");

        var result = await service.SetActiveAsync(stored, isActive: true, Actor);

        Assert.True(result is InvoiceConfigurationConflict);
    }

    [Fact]
    public async Task Restore_ReturnsConflict_WhenEtagIsStale()
    {
        var current = Configurations.Build(isActive: false);
        var repository = new FakeConfigurationRepository(current);
        var service = new InvoiceConfigurationService(repository);
        var revision = new InvoiceConfigurationRevision(
            "revision-1", current.Id, current.IntegrationType,
            InvoiceConfigurationRevisionAction.Updated, DateTimeOffset.UtcNow,
            "old-actor", "Old actor", current with { InvoiceDescription = "Historical" });

        var result = await service.RestoreAsync(new(current, "stale-etag"), revision, Actor);

        Assert.True(result is InvoiceConfigurationConflict);
    }

    [Fact]
    public async Task Update_RejectsRetryWithPreUpdateEtag_AfterASuccessfulUpdate()
    {
        // FakeConfigurationRepository must rotate its etag on every successful write, exactly
        // like Cosmos does - otherwise a real regression that skipped the optimistic-concurrency
        // check entirely would go undetected by this fake. A first update should succeed, and a
        // second update reusing the *original* (now stale) etag must be rejected as a conflict,
        // even though that etag was valid a moment ago.
        var original = Configurations.Build(isActive: false);
        var repository = new FakeConfigurationRepository(original);
        var service = new InvoiceConfigurationService(repository);
        var originalEtag = (await repository.ListAllAsync()).Single().ETag;
        var firstUpdate = original with { InvoiceDescription = "First update" };
        var secondUpdate = original with { InvoiceDescription = "Second update" };

        var firstResult = await service.UpdateAsync(original, firstUpdate, originalEtag, Actor);
        Assert.True(firstResult is StoredInvoiceConfiguration);

        var secondResult = await service.UpdateAsync(original, secondUpdate, originalEtag, Actor);

        Assert.True(secondResult is InvoiceConfigurationConflict);
    }

    [Fact]
    public async Task Update_RejectsSameSearchCriteriaAsAnotherConfiguration()
    {
        var other = Configurations.Build(
            id: new("other-config"),
            integrationConfiguration: new GraphEmailIntegrationConfiguration("sender@example.com", "Invoice \\d+"));
        var original = Configurations.Build(
            id: new("editing-config"),
            isActive: false,
            integrationConfiguration: new GraphEmailIntegrationConfiguration("sender@example.com", "Different \\d+"));
        var repository = new FakeConfigurationRepository(other, original);
        var service = new InvoiceConfigurationService(repository);
        var updated = original with
        {
            IntegrationConfiguration = new GraphEmailIntegrationConfiguration("sender@example.com", "Invoice \\d+"),
        };

        var result = await service.UpdateAsync(original, updated, "etag-editing-config-0", Actor);

        Assert.True(result is DuplicateInvoiceConfigurationSearchCriteria conflict && conflict.ConflictingId == other.Id);
    }

    [Fact]
    public async Task Restore_RejectsSearchCriteriaNowUsedByAnotherConfiguration()
    {
        // The revision being restored predates "other-config"; restoring it would recreate the
        // duplicate this feature is meant to prevent, so this must be checked on Restore too, not
        // just Create/Update.
        var other = Configurations.Build(
            id: new("other-config"),
            integrationConfiguration: new GraphEmailIntegrationConfiguration("sender@example.com", "Invoice \\d+"));
        var current = Configurations.Build(
            id: new("editing-config"),
            integrationConfiguration: new GraphEmailIntegrationConfiguration("sender@example.com", "Different \\d+"));
        var historicalSnapshot = current with
        {
            IntegrationConfiguration = new GraphEmailIntegrationConfiguration("sender@example.com", "Invoice \\d+"),
        };
        var repository = new FakeConfigurationRepository(other, current);
        var service = new InvoiceConfigurationService(repository);
        var revision = new InvoiceConfigurationRevision(
            "revision-1", current.Id, current.IntegrationType,
            InvoiceConfigurationRevisionAction.Updated, DateTimeOffset.UtcNow,
            "old-actor", "Old actor", historicalSnapshot);

        var result = await service.RestoreAsync(new(current, "etag-editing-config-0"), revision, Actor);

        Assert.True(result is DuplicateInvoiceConfigurationSearchCriteria conflict && conflict.ConflictingId == other.Id);
    }

    [Fact]
    public async Task Restore_KeepsCurrentIdentityIntegrationAndActivationState()
    {
        var current = Configurations.Build(isActive: true);
        var historical = current with
        {
            Id = new("different-id"),
            InvoiceDescription = "Historical description",
            IsActive = false,
        };
        var repository = new FakeConfigurationRepository(current);
        var service = new InvoiceConfigurationService(repository);
        var revision = new InvoiceConfigurationRevision(
            "revision-1", current.Id, current.IntegrationType,
            InvoiceConfigurationRevisionAction.Updated, DateTimeOffset.UtcNow,
            "old-actor", "Old actor", historical);

        var result = await service.RestoreAsync(new(current, "etag-test-config-0"), revision, Actor);

        Assert.True(
            result is StoredInvoiceConfiguration restored &&
            restored.Configuration.Id == current.Id &&
            restored.Configuration.IntegrationType == current.IntegrationType &&
            restored.Configuration.IsActive &&
            restored.Configuration.InvoiceDescription == "Historical description");
    }
}
