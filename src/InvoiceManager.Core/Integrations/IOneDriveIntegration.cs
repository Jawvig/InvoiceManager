namespace InvoiceManager.Core.Integrations;

/// <summary>
/// A request to upload an invoice PDF to OneDrive.
/// </summary>
/// <param name="DestinationPath">The Graph API path of the destination folder (from the invoice configuration).</param>
/// <param name="FileName">The generated filename, including extension.</param>
/// <param name="Content">The PDF bytes to upload.</param>
public sealed record OneDriveUploadRequest(string DestinationPath, string FileName, byte[] Content);

/// <summary>
/// A request to search OneDrive for a file that already satisfies the expected
/// invoice criteria (reconciliation).
/// </summary>
/// <param name="DestinationPath">The Graph API path of the folder to search (from the invoice configuration).</param>
/// <param name="Criteria">The date/amount/currency tolerances plus expected description used to match a file.</param>
public sealed record OneDriveSearchRequest(string DestinationPath, OneDriveSearchCriteria Criteria);

/// <summary>
/// Saves invoice files to OneDrive and reconciles against files already present
/// there (manual downloads, previous partial runs). The integration owns matching
/// against OneDrive contents; the workflow records and acts on the result.
/// </summary>
public interface IOneDriveIntegration
{
    /// <summary>Uploads the PDF to the destination and returns where it now lives.</summary>
    Task<OneDriveDetails> UploadAsync(OneDriveUploadRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches the destination folder for an existing file matching <paramref name="request"/>'s
    /// criteria, returning either <see cref="NoOneDriveMatch"/> or an accepted <see cref="OneDriveMatch"/>.
    /// </summary>
    Task<OneDriveSearchResult> SearchAsync(OneDriveSearchRequest request, CancellationToken cancellationToken = default);
}
