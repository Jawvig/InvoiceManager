namespace InvoiceManager.Core.Integrations;

/// <summary>
/// Reads invoice fields (date and total) out of a PDF's own content, for sources
/// that expose no reliable metadata before the PDF is opened — currently the
/// Microsoft 365 email attachment source. VAT mode is deliberately never derived
/// here: <c>actualVatMode</c> always comes from configuration, the same rule
/// every other source follows (see <see cref="ActualInvoiceDetails"/>).
/// </summary>
public interface IInvoicePdfExtractor
{
    /// <summary>
    /// Attempts to read the invoice date and total out of <paramref name="pdfContent"/>,
    /// returning either <see cref="PdfExtractionFailed"/> or a successful
    /// <see cref="PdfExtractionSucceeded"/>.
    /// </summary>
    Task<PdfExtractionResult> ExtractAsync(byte[] pdfContent, CancellationToken cancellationToken = default);
}
