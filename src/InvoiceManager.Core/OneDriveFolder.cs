namespace InvoiceManager.Core;

/// <summary>
/// A durable reference to an existing folder in the workflow account's OneDrive,
/// identified by stable drive/item identifiers so renames and moves do not break
/// processing. <see cref="FolderPath"/> is retained only for display.
/// </summary>
public sealed record OneDriveFolder(
    string DriveId,
    string DriveName,
    string FolderItemId,
    string FolderPath)
{
    /// <summary>The Microsoft Graph API path identifying this folder by stable drive/item ID.</summary>
    public string GraphPath =>
        $"/drives/{Uri.EscapeDataString(DriveId)}/items/{Uri.EscapeDataString(FolderItemId)}";

    public override string ToString() => $"{DriveName}: {FolderPath}";
}
