namespace InvoiceManager.Core.Repositories;

public interface IInvoiceConfigurationRepository
{
    Task<IReadOnlyList<InvoiceConfiguration>> ListActiveAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredInvoiceConfiguration>> ListAllAsync(CancellationToken cancellationToken = default);
    Task<Option<StoredInvoiceConfiguration>> GetAsync(
        InvoiceConfigurationId id,
        IntegrationType integrationType,
        CancellationToken cancellationToken = default);
    Task CreateIfNotExistsAsync(InvoiceConfiguration configuration, CancellationToken cancellationToken = default);
    Task<InvoiceConfigurationCreateResult> CreateAsync(
        InvoiceConfiguration configuration,
        InvoiceConfigurationActor actor,
        CancellationToken cancellationToken = default);
    Task<InvoiceConfigurationReplaceResult> ReplaceAsync(
        InvoiceConfiguration configuration,
        string etag,
        InvoiceConfigurationRevisionAction action,
        InvoiceConfigurationActor actor,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InvoiceConfigurationRevision>> ListRevisionsAsync(
        InvoiceConfigurationId id,
        IntegrationType integrationType,
        CancellationToken cancellationToken = default);
}
