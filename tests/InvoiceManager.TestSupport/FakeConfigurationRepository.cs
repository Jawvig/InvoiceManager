using InvoiceManager.Core;
using InvoiceManager.Core.Repositories;

namespace InvoiceManager.TestSupport;

/// <summary>
/// A fixed-list configuration repository: lists the active subset of the
/// configurations it was constructed with; creation is a no-op.
/// </summary>
public sealed class FakeConfigurationRepository(params InvoiceConfiguration[] configurations)
    : IInvoiceConfigurationRepository
{
    private readonly List<InvoiceConfiguration> store = [.. configurations];
    private string sentinelETag = "sentinel-etag-0";

    /// <summary>
    /// The number of upcoming <see cref="CreateAsync"/>/<see cref="ReplaceAsync"/> calls that
    /// participate in the sentinel protocol (i.e. pass a sentinel) that should report
    /// <see cref="ValidationSentinelConflict"/> instead of committing, simulating another
    /// concurrent writer having changed the sentinel first - set this to exercise
    /// <see cref="InvoiceConfigurationService"/>'s retry-once behavior in tests. Each simulated
    /// conflict consumes one call and decrements this count; it does not touch <see cref="store"/>
    /// or the sentinel's ETag, matching what a real lost race looks like from the caller's side.
    /// </summary>
    public int SentinelConflictsToSimulate { get; set; }

    /// <summary>
    /// A configuration to add to the store at the moment the next simulated sentinel conflict is
    /// consumed - models the interleaving where a concurrent winner's own Create/Update/Restore
    /// call (the one that changed the sentinel and caused this loss) committed a configuration
    /// that only becomes visible starting with this write's retry, not its first attempt.
    /// </summary>
    public InvoiceConfiguration? RevealOnNextSentinelConflict { get; set; }

    // Tracks a per-(Id, IntegrationType) write count so every successful Create/Replace mints a
    // distinct etag - just like Cosmos rotates the etag on every write. A fake that instead
    // reused a constant "etag-{id}" would wrongly accept a retry against a stale, pre-update
    // etag, silently hiding a real regression that skips the optimistic-concurrency check.
    private readonly Dictionary<(InvoiceConfigurationId, IntegrationType), int> versions =
        configurations.ToDictionary(c => (c.Id, c.IntegrationType), _ => 0);

    public Task<IReadOnlyList<InvoiceConfiguration>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InvoiceConfiguration>>(
            store.Where(c => c.IsActive).ToList());

    public Task<IReadOnlyList<StoredInvoiceConfiguration>> ListAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StoredInvoiceConfiguration>>(
            store.Select(c => new StoredInvoiceConfiguration(c, CurrentETag(c.Id, c.IntegrationType))).ToList());

    public Task<Option<StoredInvoiceConfiguration>> GetAsync(
        InvoiceConfigurationId id,
        IntegrationType integrationType,
        CancellationToken cancellationToken = default)
    {
        var configuration = store.SingleOrDefault(c => c.Id == id && c.IntegrationType == integrationType);
        Option<StoredInvoiceConfiguration> result = configuration is null
            ? Option.None
            : new StoredInvoiceConfiguration(configuration, CurrentETag(id, integrationType));
        return Task.FromResult(result);
    }

    public Task CreateIfNotExistsAsync(InvoiceConfiguration configuration, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<ConfigurationValidationSentinel> GetValidationSentinelAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ConfigurationValidationSentinel(sentinelETag));

    public Task<InvoiceConfigurationWriteResult> CreateAsync(
        InvoiceConfiguration configuration,
        InvoiceConfigurationActor actor,
        ConfigurationValidationSentinel sentinel,
        CancellationToken cancellationToken = default)
    {
        if (TryConsumeSimulatedSentinelConflict())
            return Task.FromResult<InvoiceConfigurationWriteResult>(new ValidationSentinelConflict());
        if (store.Any(c => c.Id == configuration.Id))
            return Task.FromResult<InvoiceConfigurationWriteResult>(new DuplicateInvoiceConfigurationId(configuration.Id));

        store.Add(configuration);
        versions[(configuration.Id, configuration.IntegrationType)] = 0;
        AdvanceSentinel();
        return Task.FromResult<InvoiceConfigurationWriteResult>(
            new StoredInvoiceConfiguration(configuration, CurrentETag(configuration.Id, configuration.IntegrationType)));
    }

    public Task<InvoiceConfigurationWriteResult> ReplaceAsync(
        InvoiceConfiguration configuration,
        string etag,
        InvoiceConfigurationRevisionAction action,
        InvoiceConfigurationActor actor,
        Option<ConfigurationValidationSentinel> sentinel,
        CancellationToken cancellationToken = default)
    {
        if (sentinel is ConfigurationValidationSentinel && TryConsumeSimulatedSentinelConflict())
            return Task.FromResult<InvoiceConfigurationWriteResult>(new ValidationSentinelConflict());

        // Mirrors the real repository's optimistic-concurrency check: compares the caller's
        // etag against the current stored value, and - just as importantly - rotates to a new
        // etag on every successful write (see the `versions` field above), so a retry against
        // the pre-update etag is correctly rejected by this fake too, not just by Cosmos.
        var key = (configuration.Id, configuration.IntegrationType);
        var current = store.SingleOrDefault(
            c => c.Id == configuration.Id && c.IntegrationType == configuration.IntegrationType);
        if (current is not null && etag != CurrentETag(key.Id, key.IntegrationType))
            return Task.FromResult<InvoiceConfigurationWriteResult>(new InvoiceConfigurationConflict());

        store.RemoveAll(c => c.Id == configuration.Id && c.IntegrationType == configuration.IntegrationType);
        store.Add(configuration);
        versions[key] = versions.GetValueOrDefault(key) + 1;
        if (sentinel is ConfigurationValidationSentinel)
            AdvanceSentinel();
        return Task.FromResult<InvoiceConfigurationWriteResult>(
            new StoredInvoiceConfiguration(configuration, CurrentETag(key.Id, key.IntegrationType)));
    }

    public Task<IReadOnlyList<InvoiceConfigurationRevision>> ListRevisionsAsync(
        InvoiceConfigurationId id,
        IntegrationType integrationType,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InvoiceConfigurationRevision>>([]);

    private bool TryConsumeSimulatedSentinelConflict()
    {
        if (SentinelConflictsToSimulate <= 0)
            return false;
        SentinelConflictsToSimulate--;
        if (RevealOnNextSentinelConflict is { } revealed)
        {
            store.Add(revealed);
            RevealOnNextSentinelConflict = null;
        }
        return true;
    }

    private void AdvanceSentinel() => sentinelETag = $"sentinel-etag-{Guid.NewGuid():N}";

    private string CurrentETag(InvoiceConfigurationId id, IntegrationType integrationType) =>
        $"etag-{id}-{versions.GetValueOrDefault((id, integrationType))}";
}
