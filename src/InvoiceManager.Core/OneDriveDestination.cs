namespace InvoiceManager.Core;

/// <summary>
/// A durable reference to an existing folder in the workflow account's OneDrive.
/// Legacy configurations may contain only <see cref="DisplayPath"/>; newly selected
/// folders carry stable drive/item identifiers so renames and moves do not break processing.
/// </summary>
public sealed record OneDriveDestination(
    string DisplayPath,
    string? DriveId = null,
    string? FolderItemId = null)
{
    public bool HasStableIds =>
        !string.IsNullOrWhiteSpace(DriveId) && !string.IsNullOrWhiteSpace(FolderItemId);

    public bool IsLegacyPath => !HasStableIds;

    public string GraphPath => HasStableIds
        ? $"/drives/{Uri.EscapeDataString(DriveId!)}/items/{Uri.EscapeDataString(FolderItemId!)}"
        : DisplayPath;

    public static implicit operator OneDriveDestination(string legacyPath) => new(legacyPath);

    public override string ToString() => DisplayPath;
}
