namespace InvoiceManager.Core;

/// <summary>
/// A record representing an invoice that should exist by a particular date,
/// tracking its progress through retrieval, reconciliation, save, and beyond.
/// </summary>
public sealed record InvoiceRecord(ProcessingStatus Status, Option<DateOnly> ActualInvoiceDate)
{
    public InvoiceRecord(ProcessingStatus status)
        : this(status, new None())
    {
    }
}
