namespace InvoiceManager.Core.Repositories;

/// <summary>
/// Persistent store for invoice records.
/// </summary>
public interface IInvoiceRecordRepository
{
    /// <summary>
    /// Returns the most recent invoice record for the given configuration (by expected date),
    /// or <see cref="Option.None"/> if no records exist.
    /// </summary>
    Task<Option<InvoiceRecord>> GetMostRecentAsync(InvoiceConfigurationId configurationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <c>true</c> if a record already exists for the given configuration and expected date.
    /// </summary>
    Task<bool> ExistsAsync(InvoiceConfigurationId configurationId, DateOnly expectedDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a new invoice record.
    /// </summary>
    Task CreateAsync(InvoiceRecord record, CancellationToken cancellationToken = default);
}
