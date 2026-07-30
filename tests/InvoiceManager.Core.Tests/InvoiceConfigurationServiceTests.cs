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

        var result = await service.UpdateAsync(original, updated, "etag-editing-config", Actor);

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

        var result = await service.RestoreAsync(new(current, "etag"), revision, Actor);

        Assert.True(result is DuplicateInvoiceConfigurationSearchCriteria conflict && conflict.ConflictingId == other.Id);
    }

    [Fact]
    public async Task Create_RetriesOnce_WhenSentinelLosesRaceButNoDuplicateFound()
    {
        // Simulates a concurrent Create for a different, non-conflicting configuration ID
        // winning the race and changing the sentinel first: this configuration's write should
        // retry once, revalidate (still finds nothing), and succeed.
        var repository = new FakeConfigurationRepository { SentinelConflictsToSimulate = 1 };
        var service = new InvoiceConfigurationService(repository);
        var configuration = Configurations.Build(isActive: false);

        var result = await service.CreateAsync(configuration, Actor);

        Assert.True(result is StoredInvoiceConfiguration stored && stored.Configuration.Id == configuration.Id);
    }

    [Fact]
    public async Task Create_ReportsConflict_WhenSentinelLosesRaceOnBothAttempts()
    {
        var repository = new FakeConfigurationRepository { SentinelConflictsToSimulate = 2 };
        var service = new InvoiceConfigurationService(repository);
        var configuration = Configurations.Build(isActive: false);

        var result = await service.CreateAsync(configuration, Actor);

        Assert.True(result is InvoiceConfigurationConflict);
    }

    [Fact]
    public async Task Create_FindsDuplicateIntroducedByTheWinnerOfTheSentinelRace()
    {
        // The concurrent winner's own write (the one that changed the sentinel and caused this
        // call to lose the race) commits a configuration that conflicts with this candidate's
        // search criteria - it isn't visible on the first read, only starting with the retry, so
        // the retry's revalidation must be the one to catch it.
        var winner = Configurations.Build(
            id: new("winner-config"),
            integrationConfiguration: new MicrosoftBillingIntegrationConfiguration("account-1"));
        var repository = new FakeConfigurationRepository
        {
            SentinelConflictsToSimulate = 1,
            RevealOnNextSentinelConflict = winner,
        };
        var service = new InvoiceConfigurationService(repository);
        var candidate = Configurations.Build(
            id: new("candidate-config"),
            isActive: false,
            integrationConfiguration: new MicrosoftBillingIntegrationConfiguration("account-1"));

        var result = await service.CreateAsync(candidate, Actor);

        Assert.True(result is DuplicateInvoiceConfigurationSearchCriteria conflict && conflict.ConflictingId == winner.Id);
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

        var result = await service.RestoreAsync(new(current, "etag"), revision, Actor);

        Assert.True(
            result is StoredInvoiceConfiguration restored &&
            restored.Configuration.Id == current.Id &&
            restored.Configuration.IntegrationType == current.IntegrationType &&
            restored.Configuration.IsActive &&
            restored.Configuration.InvoiceDescription == "Historical description");
    }
}
