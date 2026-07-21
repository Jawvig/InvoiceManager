namespace InvoiceManager.Core;

/// <summary>
/// Integration-specific configuration for retrieving an invoice from Microsoft
/// Billing (the account/subscription whose invoices to fetch).
/// </summary>
public sealed record MicrosoftBillingIntegrationConfiguration(string BillingAccountId);

/// <summary>
/// Integration-specific configuration for retrieving an invoice from an email
/// attachment via Microsoft Graph. <see cref="SenderEmailAddress"/> is the exact
/// sender address a candidate email must come from; <see cref="BodyPattern"/> is
/// a regular expression a candidate email's plain-text body must match.
/// </summary>
public sealed record GraphEmailIntegrationConfiguration(string SenderEmailAddress, string BodyPattern);

/// <summary>
/// The integration-specific settings for an <see cref="InvoiceConfiguration"/>,
/// modelled as a union so a configuration cannot carry fields meaningful only to
/// a different integration type.
/// </summary>
public union IntegrationConfiguration(
    MicrosoftBillingIntegrationConfiguration,
    GraphEmailIntegrationConfiguration)
{
    /// <summary>The <see cref="Core.IntegrationType"/> this configuration corresponds to.</summary>
    public IntegrationType ToIntegrationType() =>
        this switch
        {
            MicrosoftBillingIntegrationConfiguration => IntegrationType.MicrosoftBilling,
            GraphEmailIntegrationConfiguration => IntegrationType.GraphEmail,
        };
}
