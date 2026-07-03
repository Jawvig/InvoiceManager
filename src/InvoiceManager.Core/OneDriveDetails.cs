namespace InvoiceManager.Core;

/// <summary>
/// Where the invoice file lives in OneDrive. Extend with further fields
/// (file ID) as the save and reconciliation features land.
/// </summary>
public sealed record OneDriveDetails(string OneDriveLocation);
