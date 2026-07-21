using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using InvoiceManager.Core;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InvoiceManager.AdminWeb.Pages.Configurations;

public abstract class ConfigurationFormPageModel(IMicrosoftResourceDiscovery discovery) : PageModel
{
    protected IMicrosoftResourceDiscovery Discovery { get; } = discovery;
    public abstract ConfigurationFormInput Input { get; set; }
    public abstract bool IsEdit { get; }
    public IReadOnlyList<BillingAccountChoice> BillingAccounts { get; protected set; } = [];
    public IReadOnlyList<string> DiscoveryWarnings => discoveryWarnings;
    private readonly List<string> discoveryWarnings = [];
    public bool ShowMissingBillingWarning => IsEdit && Input.IntegrationType != IntegrationType.GraphEmail &&
        !BillingAccounts.Any(x => x.Id == Input.OriginalBillingAccountId);

    protected async Task LoadDiscoveryAsync(CancellationToken cancellationToken, bool required = true)
    {
        if (Input.IntegrationType != IntegrationType.GraphEmail)
        {
            try
            {
                BillingAccounts = await Discovery.ListBillingAccountsAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var message = $"Billing-account discovery failed: {ex.Message}";
                if (required) ModelState.AddModelError("Input.BillingAccountId", message);
                else discoveryWarnings.Add(message + " An unchanged stored account may be preserved.");
            }
        }
    }
}
