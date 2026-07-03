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
    public Task<IReadOnlyList<InvoiceConfiguration>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InvoiceConfiguration>>(
            configurations.Where(c => c.IsActive).ToList());

    public Task CreateIfNotExistsAsync(InvoiceConfiguration configuration, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
