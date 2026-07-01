namespace InvoiceManager.Core.Repositories;

public interface IInvoiceConfigurationRepository
{
    Task<IReadOnlyList<InvoiceConfiguration>> ListActiveAsync(CancellationToken cancellationToken = default);
    Task CreateIfNotExistsAsync(InvoiceConfiguration configuration, CancellationToken cancellationToken = default);
}
