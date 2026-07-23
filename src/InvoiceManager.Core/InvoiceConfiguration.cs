namespace InvoiceManager.Core;

/// <summary>
/// A configuration entry describing an invoice that the service expects to
/// retrieve on a recurring basis.
/// </summary>
public sealed record InvoiceConfiguration(
    InvoiceConfigurationId Id,
    IntegrationConfiguration IntegrationConfiguration,
    string InvoiceDescription,
    InvoiceFrequency Frequency,
    Option<AmountMatchingCriteria> AmountMatchingCriteria,
    VatMode DefaultVatMode,
    bool IsActive,
    OneDriveFolder OneDriveFolder,
    DateOnly StartDate,
    int DateToleranceDays)
{
    /// <summary>The integration type this configuration uses, derived from <see cref="IntegrationConfiguration"/>.</summary>
    public IntegrationType IntegrationType => IntegrationConfiguration.ToIntegrationType();
}
