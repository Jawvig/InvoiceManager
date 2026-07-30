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
            TestDatabase, new ContainerProperties("invoice-configurations", "/partitionKey"));

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
        // BuildConfiguration's defaults (same billing account, same amount criteria) make any two
        // distinct-ID configurations built by it conflict per ValidateNoDuplicateMatch, which
        // CreateIfNotExistsAsync now enforces (see the sentinel-protocol tests below) - give this
        // one distinct search criteria since this test is only about the isActive filter, not
        // duplicate detection.
        var inactive = BuildConfiguration(new InvoiceConfigurationId("list-inactive"), isActive: false) with
        {
            IntegrationConfiguration = new MicrosoftBillingIntegrationConfiguration("list-inactive-account"),
        };
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
        var sentinel = await repository!.GetValidationSentinelAsync();
        var created = await repository.CreateAsync(draft, actor, sentinel) switch
        {
            StoredInvoiceConfiguration value => value,
            var other => throw new Xunit.Sdk.XunitException($"Expected a successful create, got {other}."),
        };

        var updated = draft with { InvoiceDescription = "Updated" };
        var sentinelForUpdate = await repository.GetValidationSentinelAsync();
        await repository.ReplaceAsync(
            updated, created.ETag, InvoiceConfigurationRevisionAction.Updated, actor, sentinelForUpdate);

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
    public async Task Create_RejectsDuplicateIdAcrossIntegrationTypes()
    {
        var actor = new InvoiceConfigurationActor("actor-1", "Admin User");
        var original = BuildConfiguration(new("duplicate-id"), isActive: false);
        await repository!.CreateAsync(original, actor, await repository.GetValidationSentinelAsync());
        var duplicate = original with
        {
            IntegrationConfiguration = new GraphEmailIntegrationConfiguration("sender@example.com", "Invoice"),
        };

        var result = await repository.CreateAsync(duplicate, actor, await repository.GetValidationSentinelAsync());

        Assert.True(result is DuplicateInvoiceConfigurationId conflict && conflict.Id == original.Id);
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
            new("actor-1", "Admin User"),
            await repository.GetValidationSentinelAsync());

        var revisions = await repository.ListRevisionsAsync(configuration.Id, configuration.IntegrationType);
        Assert.Equal(InvoiceConfigurationRevisionAction.PreAuditBaseline, revisions[0].Action);
        Assert.Null(revisions[0].ActorObjectId);
        Assert.Equal(InvoiceConfigurationRevisionAction.Deactivated, revisions[1].Action);
    }

    [Fact]
    public async Task Replace_RejectsStaleEtag()
    {
        var configuration = BuildConfiguration(new("etag-conflict"), isActive: false);
        var stored = await repository!.CreateAsync(
            configuration, new("actor", "Admin"), await repository.GetValidationSentinelAsync()) switch
        {
            StoredInvoiceConfiguration value => value,
            var other => throw new Xunit.Sdk.XunitException($"Expected a successful create, got {other}."),
        };
        await repository.ReplaceAsync(
            configuration with { InvoiceDescription = "First" }, stored.ETag,
            InvoiceConfigurationRevisionAction.Updated, new("actor", "Admin"),
            await repository.GetValidationSentinelAsync());

        var result = await repository.ReplaceAsync(
            configuration with { InvoiceDescription = "Stale" }, stored.ETag,
            InvoiceConfigurationRevisionAction.Updated, new("actor", "Admin"),
            await repository.GetValidationSentinelAsync());

        Assert.True(result is InvoiceConfigurationConflict, $"Expected InvoiceConfigurationConflict, got {result}.");
    }

    [Fact]
    public async Task GetValidationSentinelAsync_BootstrapsSentinel_WhenContainerIsFresh()
    {
        var first = await repository!.GetValidationSentinelAsync();
        var second = await repository.GetValidationSentinelAsync();

        Assert.False(string.IsNullOrWhiteSpace(first.ETag));
        Assert.Equal(first.ETag, second.ETag);
    }

    [Fact]
    public async Task Create_ReportsSentinelConflict_WhenSentinelChangedSinceItWasRead()
    {
        var sentinel = await repository!.GetValidationSentinelAsync();
        // Simulate another writer committing first and changing the sentinel's ETag by writing
        // through it directly, without going through this stale copy.
        var otherWriterConfig = BuildConfiguration(new("other-writer-config"), isActive: false);
        await repository.CreateAsync(otherWriterConfig, new("actor", "Admin"), sentinel);

        var configuration = BuildConfiguration(new("late-writer-config"), isActive: false);
        var result = await repository.CreateAsync(configuration, new("actor", "Admin"), sentinel);

        Assert.True(result is ValidationSentinelConflict, $"Expected ValidationSentinelConflict, got {result}.");
        // The configuration document itself must not have been committed - the whole
        // transactional batch, including the config + revision writes, rolled back together.
        Assert.True((await repository.GetAsync(configuration.Id, configuration.IntegrationType)) is None);
    }

    [Fact]
    public async Task ConcurrentCreates_OnlyOneSucceeds_WhenBothReadTheSameSentinel()
    {
        var sentinel = await repository!.GetValidationSentinelAsync();
        var first = BuildConfiguration(new("race-config-a"), isActive: false);
        var second = BuildConfiguration(new("race-config-b"), isActive: false);

        var results = await Task.WhenAll(
            repository.CreateAsync(first, new("actor", "Admin"), sentinel),
            repository.CreateAsync(second, new("actor", "Admin"), sentinel));

        Assert.Single(results, r => r is StoredInvoiceConfiguration);
        Assert.Single(results, r => r is ValidationSentinelConflict);
    }

    [Fact]
    public async Task CreateIfNotExistsAsync_AdvancesSentinel_OnSuccessfulInsert()
    {
        var before = await repository!.GetValidationSentinelAsync();

        await repository.CreateIfNotExistsAsync(BuildConfiguration(new("seeded-config"), isActive: false));

        var after = await repository.GetValidationSentinelAsync();
        Assert.NotEqual(before.ETag, after.ETag);
    }

    [Fact]
    public async Task CreateIfNotExistsAsync_LeavesSentinelUnchanged_WhenAlreadySeeded()
    {
        var configuration = BuildConfiguration(new("already-seeded-config"), isActive: false);
        await repository!.CreateIfNotExistsAsync(configuration);
        var before = await repository.GetValidationSentinelAsync();

        // Bootstrap seeding is insert-only - re-seeding the same ID must be a true no-op,
        // including leaving the sentinel alone (nothing new was actually introduced).
        await repository.CreateIfNotExistsAsync(configuration with { InvoiceDescription = "Changed" });

        var after = await repository.GetValidationSentinelAsync();
        Assert.Equal(before.ETag, after.ETag);
    }

    [Fact]
    public async Task CreateIfNotExistsAsync_IsStillANoOp_WhenTheSeededConfigurationsCriteriaHasDriftedToAnotherConfiguration()
    {
        // Regression test for the exact scenario the existing-ID no-op must protect against:
        // seed ID "drifted-seed-config" was originally seeded with BuildConfiguration's default
        // search criteria (same billing account "test:billing:account" as everything else built by
        // it), then an admin edited it via AdminWeb (simulated here with ReplaceAsync) to use
        // different criteria, freeing up the original criteria. Some other configuration
        // ("claims-original-criteria") was then legitimately created using those now-free original
        // criteria - nothing wrong with that on its own. Re-running the seeder for
        // "drifted-seed-config" (e.g. on a redeploy) must remain the same harmless no-op it always
        // was, even though the seed file's original criteria now genuinely match a different live
        // configuration - it must not be misreported as a conflict.
        var originalSeedConfiguration = BuildConfiguration(new("drifted-seed-config"), isActive: false);
        await repository!.CreateIfNotExistsAsync(originalSeedConfiguration);
        var stored = await repository.GetAsync(originalSeedConfiguration.Id, originalSeedConfiguration.IntegrationType) switch
        {
            StoredInvoiceConfiguration value => value,
            var other => throw new Xunit.Sdk.XunitException($"Expected the seeded configuration, got {other}."),
        };

        // The admin edit: move "drifted-seed-config" to different search criteria, freeing up the
        // original billing account for someone else to use.
        var editedConfiguration = originalSeedConfiguration with
        {
            IntegrationConfiguration = new MicrosoftBillingIntegrationConfiguration("drifted-seed-new-account"),
        };
        await repository.ReplaceAsync(
            editedConfiguration, stored.ETag, InvoiceConfigurationRevisionAction.Updated, new("actor", "Admin"),
            Option.None);

        // Another, unrelated configuration legitimately claims the now-free original criteria.
        var claimant = BuildConfiguration(new("claims-original-criteria"), isActive: false);
        await repository.CreateAsync(claimant, new("actor", "Admin"), await repository.GetValidationSentinelAsync());

        // Re-running the seeder for the original seed record (unchanged from the seed file, so
        // still carrying the now-superseded original criteria) must still be a pure no-op.
        var exception = await Record.ExceptionAsync(() => repository.CreateIfNotExistsAsync(originalSeedConfiguration));
        Assert.Null(exception);

        var afterReseed = await repository.GetAsync(originalSeedConfiguration.Id, originalSeedConfiguration.IntegrationType) switch
        {
            StoredInvoiceConfiguration value => value,
            var other => throw new Xunit.Sdk.XunitException($"Expected the configuration to still exist, got {other}."),
        };
        Assert.True(
            afterReseed.Configuration.IntegrationConfiguration is MicrosoftBillingIntegrationConfiguration billing &&
            billing.BillingAccountId == "drifted-seed-new-account",
            $"Expected the edited billing account to still be in effect, got {afterReseed.Configuration.IntegrationConfiguration}.");
    }

    [Fact]
    public async Task Seeding_DuringADeployWindow_InvalidatesAConcurrentAdminWebWriteThatReadTheSentinelFirst()
    {
        // Models the exact race scripts/Deploy-Infra.ps1 can produce: terraform apply can start
        // routing traffic to a live AdminWeb instance before the seeder runs, so an admin's
        // Create/Update/Restore request can read the sentinel (and, in InvoiceConfigurationService,
        // the configuration list) *before* the seeder inserts a new configuration - the same
        // read-then-write race the sentinel exists to close, just with the seeder as the other
        // writer instead of another AdminWeb request. This exercises the repository-level
        // mechanics that make that possible to detect: CreateIfNotExistsAsync's successful insert
        // must advance the sentinel, so the admin's now-stale sentinel copy is rejected.
        var adminWebSentinelReadBeforeSeeding = await repository!.GetValidationSentinelAsync();

        await repository.CreateIfNotExistsAsync(BuildConfiguration(new("seeded-during-deploy"), isActive: false));

        // The admin's request, still holding its now-stale sentinel read from before seeding,
        // tries to save an unrelated configuration - even though it wouldn't itself conflict with
        // what the seeder just added, it must still lose, because InvoiceConfigurationService
        // (the actual caller) hasn't had a chance to revalidate against the now-current list yet.
        var adminWebConfiguration = BuildConfiguration(new("admin-web-config"), isActive: false);
        var result = await repository.CreateAsync(
            adminWebConfiguration, new("actor", "Admin"), adminWebSentinelReadBeforeSeeding);

        Assert.True(result is ValidationSentinelConflict, $"Expected ValidationSentinelConflict, got {result}.");
    }

    [Fact]
    public async Task CreateIfNotExistsAsync_ThrowsSeedConflict_WhenALiveConfigurationAlreadyHasConflictingCriteria()
    {
        // The reverse race ordering from the test above: an AdminWeb write commits conflicting
        // search criteria *before* the seeder ever attempts its insert (the simplest case of
        // "before the seeder's very first attempt, or between a lost sentinel race and its
        // retry" - both paths go through the exact same revalidate-then-insert check on every
        // attempt, so pinning the live conflict down before attempt 1 exercises the same code the
        // retry path would). BuildConfiguration's defaults (same billing account, same amount
        // criteria) make any two distinct-ID configurations built by it conflict.
        var adminWebConfiguration = BuildConfiguration(new("admin-committed-first"), isActive: false);
        await repository!.CreateAsync(
            adminWebConfiguration, new("actor", "Admin"), await repository.GetValidationSentinelAsync());

        var seedConfiguration = BuildConfiguration(new("seed-conflicts-with-admin"), isActive: false);
        await Assert.ThrowsAsync<SeedConfigurationConflictException>(
            () => repository.CreateIfNotExistsAsync(seedConfiguration));

        // The seed configuration must not have been inserted alongside the conflicting one.
        Assert.True(
            (await repository.GetAsync(seedConfiguration.Id, seedConfiguration.IntegrationType)) is None);
    }

    [Fact]
    public async Task Seeding_ConcurrentWithAConflictingAdminWebWrite_NeverStoresBothConflictingConfigurations()
    {
        // A genuine Task.WhenAll race (like ConcurrentCreates_OnlyOneSucceeds_... above) between a
        // simulated AdminWeb Create and the seeder's CreateIfNotExistsAsync for two configurations
        // that conflict with each other's search criteria. Whichever ordering the real emulator
        // produces, the two conflicting configurations must never both end up stored: if the
        // seeder wins the sentinel race first, the AdminWeb write must lose with
        // ValidationSentinelConflict (its own caller, InvoiceConfigurationService, would then
        // revalidate and report the duplicate); if the AdminWeb write wins first, the seeder must
        // detect the now-conflicting live configuration - on its first attempt or a retry after
        // losing its own sentinel race - and throw SeedConfigurationConflictException instead of
        // inserting on top of it.
        var adminWebSentinel = await repository!.GetValidationSentinelAsync();
        var seedConfiguration = BuildConfiguration(new("race-seed-config"), isActive: false);
        var adminWebConfiguration = BuildConfiguration(new("race-adminweb-config"), isActive: false);

        var seedTask = SeedIgnoringExpectedConflictAsync(repository, seedConfiguration);
        var adminWebTask = repository.CreateAsync(adminWebConfiguration, new("actor", "Admin"), adminWebSentinel);

        await Task.WhenAll(seedTask, adminWebTask);

        var seedStored = await repository.GetAsync(seedConfiguration.Id, seedConfiguration.IntegrationType)
            is StoredInvoiceConfiguration;
        var adminWebStored = await repository.GetAsync(adminWebConfiguration.Id, adminWebConfiguration.IntegrationType)
            is StoredInvoiceConfiguration;

        Assert.False(
            seedStored && adminWebStored,
            "Both conflicting configurations were stored - the invariant the sentinel protocol exists to protect was violated.");
    }

    private static async Task SeedIgnoringExpectedConflictAsync(
        CosmosInvoiceConfigurationRepository repository, InvoiceConfiguration configuration)
    {
        try
        {
            await repository.CreateIfNotExistsAsync(configuration);
        }
        catch (SeedConfigurationConflictException)
        {
            // Expected when the concurrent AdminWeb write wins the race and commits first.
        }
    }

    private static InvoiceConfiguration BuildConfiguration(
        InvoiceConfigurationId id,
        string invoiceDescription = "Test Invoice",
        bool isActive = true) =>
        new(
            id,
            new MicrosoftBillingIntegrationConfiguration("test:billing:account"),
            invoiceDescription,
            InvoiceFrequency.Monthly,
            new AmountMatchingCriteria(new Money(10.00m, "GBP"), 0m),
            VatMode.Exclusive,
            IsActive: isActive,
            OneDriveFolder: new OneDriveFolder("test-drive", "Test Drive", "test-folder-item", "/Bills/Test"),
            StartDate: new DateOnly(2025, 1, 1),
            DateToleranceDays: 5);
}
