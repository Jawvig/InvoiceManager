using NodaMoney;

namespace InvoiceManager.Core.Integrations;

/// <summary>
/// The PDF could not be read confidently enough to produce a usable invoice
/// date and total (a technical failure, low-confidence fields, or fields
/// missing entirely). Callers treat this the same as any other retrieval
/// failure — <c>RetrievalError</c>, always retryable.
/// </summary>
public sealed record PdfExtractionFailed(string Reason);

/// <summary>
/// The PDF's invoice date and total were read successfully. VAT mode is
/// deliberately not part of this result — it always comes from configuration.
/// </summary>
public sealed record PdfExtractionSucceeded(DateOnly InvoiceDate, Money Total);

/// <summary>The outcome of asking an <see cref="IInvoicePdfExtractor"/> to read a PDF.</summary>
public union PdfExtractionResult(PdfExtractionFailed, PdfExtractionSucceeded);
