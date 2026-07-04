using NodaMoney;

namespace InvoiceManager.Core;

/// <summary>
/// Values read from the actual invoice once it has been found, as opposed to
/// the expected values used to search for it. The VAT mode is deliberately not
/// recorded here — it is taken from configuration when generating filenames.
/// </summary>
public sealed record ActualInvoiceDetails(
    DateOnly ActualInvoiceDate,
    Money ActualAmount,
    SourceInvoiceId SourceInvoiceId);
