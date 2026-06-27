namespace InvoiceManager.Core;

/// <summary>
/// A record representing an invoice that should exist by a particular date,
/// tracking its progress through retrieval, reconciliation, save, and beyond.
/// </summary>
public sealed record InvoiceRecord
{
    /// <summary>The current processing state of this record.</summary>
    public required ProcessingStatus Status { get; init; }

    /// <summary>
    /// The invoice date observed after retrieval or reconciliation. Absent until
    /// the invoice has actually been found.
    /// </summary>
    public Option<DateOnly> ActualInvoiceDate { get; init; } = new None();
}
