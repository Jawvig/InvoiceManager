namespace InvoiceManager.Core;

/// <summary>
/// A configuration entry describing an invoice that the service expects to
/// retrieve on a recurring basis.
/// </summary>
/// <param name="SenderEmailAddress">
/// For <see cref="Core.IntegrationType.Microsoft365Email"/>, the exact sender
/// address a candidate email must come from. Empty and unused for other
/// integration types.
/// </param>
/// <param name="BodyPattern">
/// For <see cref="Core.IntegrationType.Microsoft365Email"/>, a regular
/// expression a candidate email's plain-text body must match. Empty and
/// unused for other integration types.
/// </param>
public sealed record InvoiceConfiguration(
    InvoiceConfigurationId Id,
    IntegrationType IntegrationType,
    string InvoiceDescription,
    InvoiceFrequency Frequency,
    Option<AmountMatchingCriteria> AmountMatchingCriteria,
    VatMode DefaultVatMode,
    bool IsActive,
    OneDriveDestination OneDriveDestination,
    DateOnly StartDate,
    string BillingAccountId,
    int DateToleranceDays,
    string SenderEmailAddress = "",
    string BodyPattern = "");
