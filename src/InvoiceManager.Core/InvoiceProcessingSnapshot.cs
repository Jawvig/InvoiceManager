namespace InvoiceManager.Core;

/// <summary>
/// Processing-relevant configuration copied onto an expected record when it is generated.
/// This keeps later configuration edits future-only.
/// </summary>
public sealed record InvoiceProcessingSnapshot(
    IntegrationType IntegrationType,
    string BillingAccountId,
    OneDriveDestination OneDriveDestination,
    string InvoiceDescription,
    int DateToleranceDays,
    Option<AmountMatchingCriteria> AmountMatchingCriteria,
    VatMode VatMode,
    string SenderEmailAddress = "",
    string BodyPattern = "")
{
    public static InvoiceProcessingSnapshot FromConfiguration(InvoiceConfiguration configuration) =>
        new(
            configuration.IntegrationType,
            configuration.BillingAccountId,
            configuration.OneDriveDestination,
            configuration.InvoiceDescription,
            configuration.DateToleranceDays,
            configuration.AmountMatchingCriteria,
            configuration.DefaultVatMode,
            configuration.SenderEmailAddress,
            configuration.BodyPattern);
}
