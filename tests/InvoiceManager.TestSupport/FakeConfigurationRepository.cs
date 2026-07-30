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

    public Task<IReadOnlyList<InvoiceConfiguration>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InvoiceConfiguration>>(
            store.Where(c => c.IsActive).ToList());

    public Task<IReadOnlyList<StoredInvoiceConfiguration>> ListAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StoredInvoiceConfiguration>>(
            store.Select(c => new StoredInvoiceConfiguration(c, $"etag-{c.Id}")).ToList());

    public Task<Option<StoredInvoiceConfiguration>> GetAsync(
        InvoiceConfigurationId id,
        IntegrationType integrationType,
        CancellationToken cancellationToken = default)
    {
        var configuration = store.SingleOrDefault(c => c.Id == id && c.IntegrationType == integrationType);
        Option<StoredInvoiceConfiguration> result = configuration is null
            ? Option.None
            : new StoredInvoiceConfiguration(configuration, $"etag-{configuration.Id}");
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
        InvoiceConfigurationCreateResult created = new StoredInvoiceConfiguration(configuration, $"etag-{configuration.Id}");
        return Task.FromResult(created);
    }

    public Task<InvoiceConfigurationReplaceResult> ReplaceAsync(
        InvoiceConfiguration configuration,
        string etag,
        InvoiceConfigurationRevisionAction action,
        InvoiceConfigurationActor actor,
        CancellationToken cancellationToken = default)
    {
        // Mirrors the real repository's optimistic-concurrency check (compares the caller's
        // etag against the current stored value, computed the same deterministic way GetAsync/
        // ListAllAsync do) so tests can trigger InvoiceConfigurationConflict by passing a stale
        // etag, without needing the Cosmos emulator.
        var current = store.SingleOrDefault(
            c => c.Id == configuration.Id && c.IntegrationType == configuration.IntegrationType);
        if (current is not null && etag != $"etag-{current.Id}")
        {
            InvoiceConfigurationReplaceResult conflict = new InvoiceConfigurationConflict();
            return Task.FromResult(conflict);
        }

        store.RemoveAll(c => c.Id == configuration.Id && c.IntegrationType == configuration.IntegrationType);
        store.Add(configuration);
        InvoiceConfigurationReplaceResult replaced =
            new StoredInvoiceConfiguration(configuration, $"etag-{configuration.Id}");
        return Task.FromResult(replaced);
    }

    public Task<IReadOnlyList<InvoiceConfigurationRevision>> ListRevisionsAsync(
        InvoiceConfigurationId id,
        IntegrationType integrationType,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InvoiceConfigurationRevision>>([]);
}
