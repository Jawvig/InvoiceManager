namespace InvoiceManager.Core.Integrations;

/// <summary>
/// A request to upload an invoice PDF to OneDrive.
/// </summary>
/// <param name="DestinationPath">The Graph API path of the destination folder (from the invoice configuration).</param>
/// <param name="FileName">The generated filename, including extension.</param>
/// <param name="Content">The PDF bytes to upload.</param>
public sealed record OneDriveUploadRequest(string DestinationPath, string FileName, byte[] Content);

/// <summary>
/// Saves invoice files to OneDrive. Only upload is defined here; searching
/// OneDrive for existing files (reconciliation) is a separate concern added later.
/// </summary>
public interface IOneDriveIntegration
{
    /// <summary>Uploads the PDF to the destination and returns where it now lives.</summary>
    Task<OneDriveDetails> UploadAsync(OneDriveUploadRequest request, CancellationToken cancellationToken = default);
}
