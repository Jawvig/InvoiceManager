namespace InvoiceManager.Core;

/// <summary>
/// Processing-relevant configuration copied onto an expected record when it is generated.
/// This keeps later configuration edits future-only.
/// </summary>
public sealed record InvoiceProcessingSnapshot(
    IntegrationConfiguration IntegrationConfiguration,
    OneDriveFolder OneDriveFolder,
    string InvoiceDescription,
    int DateToleranceDays,
    Option<AmountMatchingCriteria> AmountMatchingCriteria,
    VatMode VatMode)
{
    public IntegrationType IntegrationType => IntegrationConfiguration.ToIntegrationType();

    public static InvoiceProcessingSnapshot FromConfiguration(InvoiceConfiguration configuration) =>
        new(
            configuration.IntegrationConfiguration,
            configuration.OneDriveFolder,
            configuration.InvoiceDescription,
            configuration.DateToleranceDays,
            configuration.AmountMatchingCriteria,
            configuration.DefaultVatMode);
}
