using System.Text.Json.Serialization;

namespace InvoiceManager.Seeder;

/// <summary>
/// The JSON shape used in <c>data/seed/invoice-configurations.json</c>.
/// Keeps a flat, hand-editable shape: <see cref="BillingAccountId"/> is used for
/// <c>MicrosoftBilling</c> configurations, <see cref="SenderEmailAddress"/>/
/// <see cref="BodyPattern"/> for <c>GraphEmail</c> ones.
/// </summary>
internal sealed record SeedInvoiceConfigurationRecord(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string IntegrationType,
    [property: JsonRequired] string InvoiceDescription,
    [property: JsonRequired] string Frequency,
    SeedAmountMatchingCriteria? AmountMatchingCriteria,
    [property: JsonRequired] string DefaultVatMode,
    [property: JsonRequired] bool IsActive,
    [property: JsonRequired] SeedOneDriveFolder OneDriveFolder,
    [property: JsonRequired] string StartDate,
    [property: JsonRequired] int DateToleranceDays,
    string BillingAccountId = "",
    string SenderEmailAddress = "",
    string BodyPattern = "");

internal sealed record SeedAmountMatchingCriteria(
    [property: JsonRequired] decimal Amount,
    [property: JsonRequired] string Currency,
    [property: JsonRequired] decimal AmountTolerance);

/// <summary>
/// The seed JSON shape for a OneDrive folder. <see cref="DriveId"/> and
/// <see cref="FolderItemId"/> are placeholder tokens (e.g.
/// <c>REPLACE_WITH_MICROSOFT365_FOLDER_ITEM_ID</c>) substituted at seed time from
/// the <c>InvoiceManager__Seed__*</c> environment variables — resolved
/// automatically from the target folders' paths by
/// <c>tools/dev-setup/Set-SeedEnvironment.ps1</c> via Microsoft Graph, not
/// gathered manually.
/// </summary>
internal sealed record SeedOneDriveFolder(
    [property: JsonRequired] string DriveId,
    [property: JsonRequired] string DriveName,
    [property: JsonRequired] string FolderItemId,
    [property: JsonRequired] string FolderPath);
