namespace InvoiceManager.Integrations.Microsoft365;

/// <summary>
/// Tunable settings for the Microsoft 365 (Azure Billing) invoice source.
/// </summary>
public sealed class MicrosoftBillingOptions
{
    public const string SectionName = "MicrosoftBilling";

    /// <summary>The Azure Billing REST API version.</summary>
    public string ApiVersion { get; set; } = "2024-04-01";

    /// <summary>
    /// The delegated scope requested for the Azure Billing API. Must match a scope
    /// the admin account has consented to (see the admin website sign-in).
    /// </summary>
    public string Scope { get; set; } = "https://management.azure.com/user_impersonation";

    /// <summary>
    /// How long to wait between download-status polls when the response carries no
    /// <c>Retry-After</c> header.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The overall time budget for polling the document download to completion.</summary>
    public TimeSpan MaxPollDuration { get; set; } = TimeSpan.FromMinutes(5);
}
