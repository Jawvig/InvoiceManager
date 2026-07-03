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
    /// Persists a new invoice record. If a record with the same ID already exists (i.e. the same
    /// configuration and expected date), the call is a no-op — the existing record is not overwritten.
    /// </summary>
    Task CreateIfNotExistsAsync(InvoiceRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every record still awaiting processing whose expected date is on
    /// or before <paramref name="asOf"/>, across all configurations. This
    /// includes records still awaiting retrieval and records retrieved before a
    /// previous save attempt failed.
    /// </summary>
    Task<IReadOnlyList<InvoiceRecord>> ListDueAsync(DateOnly asOf, CancellationToken cancellationToken = default);

    /// <summary>
    /// Overwrites an existing invoice record with an updated state. Used to advance a record
    /// through its workflow (for example from <see cref="Expected"/> to <see cref="Retrieved"/>).
    /// </summary>
    Task ReplaceAsync(InvoiceRecord record, CancellationToken cancellationToken = default);
}
