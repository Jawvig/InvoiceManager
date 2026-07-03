namespace InvoiceManager.Core;

/// <summary>
/// Values read from the actual invoice once it has been found, as opposed to
/// the expected values used to search for it. Extend with further fields
/// (amount, currency, VAT mode, source invoice ID) as retrieval features land.
/// </summary>
public sealed record ActualInvoiceDetails(DateOnly ActualInvoiceDate);
