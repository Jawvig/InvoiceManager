namespace InvoiceManager.Core.Repositories;

public interface IInvoiceConfigurationRepository
{
    Task<IReadOnlyList<InvoiceConfiguration>> ListActiveAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredInvoiceConfiguration>> ListAllAsync(CancellationToken cancellationToken = default);
    Task<Option<StoredInvoiceConfiguration>> GetAsync(
        InvoiceConfigurationId id,
        IntegrationType integrationType,
        CancellationToken cancellationToken = default);
    /// <summary>
    /// Bootstrap seeding: inserts <paramref name="configuration"/> unless a configuration with the
    /// same ID already exists, in which case this is a no-op - insert-only, never overwrites
    /// UI-managed values. A successful insert also advances the duplicate-validation sentinel
    /// atomically with it (see <see cref="ConfigurationValidationSentinel"/>): a deploy can run the
    /// seeder while a live AdminWeb instance is still serving requests, so this participates in the
    /// same sentinel protocol as <see cref="CreateAsync"/>/<see cref="ReplaceAsync"/> rather than
    /// being exempt from it.
    /// </summary>
    Task CreateIfNotExistsAsync(InvoiceConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the current duplicate-validation sentinel document, creating it first if this is the
    /// very first call against a fresh container. See
    /// <see cref="ConfigurationValidationSentinel"/> and docs/data-model.md's
    /// "Duplicate-validation sentinel" section.
    /// </summary>
    Task<ConfigurationValidationSentinel> GetValidationSentinelAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the configuration + its "Created" revision, and conditionally replaces the
    /// duplicate-validation sentinel (using <paramref name="sentinel"/>'s ETag) in the same
    /// transactional batch, so a concurrent write that changed the sentinel first surfaces as
    /// <see cref="ValidationSentinelConflict"/> instead of silently letting both callers' earlier
    /// duplicate-search-criteria validation stand.
    /// </summary>
    Task<InvoiceConfigurationWriteResult> CreateAsync(
        InvoiceConfiguration configuration,
        InvoiceConfigurationActor actor,
        ConfigurationValidationSentinel sentinel,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the configuration + appends its revision, conditioned on <paramref name="etag"/>
    /// as today. When <paramref name="sentinel"/> is present, also conditionally replaces the
    /// duplicate-validation sentinel in the same transactional batch (see the overload above) -
    /// pass <see cref="Option.None"/> for mutations that don't revalidate duplicate search
    /// criteria (e.g. activate/deactivate), which have no sentinel race to protect against.
    /// </summary>
    Task<InvoiceConfigurationWriteResult> ReplaceAsync(
        InvoiceConfiguration configuration,
        string etag,
        InvoiceConfigurationRevisionAction action,
        InvoiceConfigurationActor actor,
        Option<ConfigurationValidationSentinel> sentinel,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InvoiceConfigurationRevision>> ListRevisionsAsync(
        InvoiceConfigurationId id,
        IntegrationType integrationType,
        CancellationToken cancellationToken = default);
}
