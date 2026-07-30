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

    public Task<InvoiceConfigurationCreateResult> CreateAsync(
        InvoiceConfiguration configuration,
        InvoiceConfigurationActor actor,
        CancellationToken cancellationToken = default)
    {
        if (store.Any(c => c.Id == configuration.Id))
        {
            InvoiceConfigurationCreateResult duplicate = new DuplicateInvoiceConfigurationId(configuration.Id);
            return Task.FromResult(duplicate);
        }

        store.Add(configuration);
        versions[(configuration.Id, configuration.IntegrationType)] = 0;
        InvoiceConfigurationCreateResult created =
            new StoredInvoiceConfiguration(configuration, CurrentETag(configuration.Id, configuration.IntegrationType));
        return Task.FromResult(created);
    }

    public Task<InvoiceConfigurationReplaceResult> ReplaceAsync(
        InvoiceConfiguration configuration,
        string etag,
        InvoiceConfigurationRevisionAction action,
        InvoiceConfigurationActor actor,
        CancellationToken cancellationToken = default)
    {
        // Mirrors the real repository's optimistic-concurrency check: compares the caller's
        // etag against the current stored value, and - just as importantly - rotates to a new
        // etag on every successful write (see the `versions` field above), so a retry against
        // the pre-update etag is correctly rejected by this fake too, not just by Cosmos.
        var key = (configuration.Id, configuration.IntegrationType);
        var current = store.SingleOrDefault(
            c => c.Id == configuration.Id && c.IntegrationType == configuration.IntegrationType);
        if (current is not null && etag != CurrentETag(key.Id, key.IntegrationType))
        {
            InvoiceConfigurationReplaceResult conflict = new InvoiceConfigurationConflict();
            return Task.FromResult(conflict);
        }

        store.RemoveAll(c => c.Id == configuration.Id && c.IntegrationType == configuration.IntegrationType);
        store.Add(configuration);
        versions[key] = versions.GetValueOrDefault(key) + 1;
        InvoiceConfigurationReplaceResult replaced =
            new StoredInvoiceConfiguration(configuration, CurrentETag(key.Id, key.IntegrationType));
        return Task.FromResult(replaced);
    }

    public Task<IReadOnlyList<InvoiceConfigurationRevision>> ListRevisionsAsync(
        InvoiceConfigurationId id,
        IntegrationType integrationType,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InvoiceConfigurationRevision>>([]);

    private string CurrentETag(InvoiceConfigurationId id, IntegrationType integrationType) =>
        $"etag-{id}-{versions.GetValueOrDefault((id, integrationType))}";
}
