using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InvoiceManager.AdminWeb.Pages.Configurations;

public abstract class ConfigurationFormPageModel(IMicrosoftResourceDiscovery discovery) : PageModel
{
    protected IMicrosoftResourceDiscovery Discovery { get; } = discovery;
    public abstract ConfigurationFormInput Input { get; set; }
    public abstract bool IsEdit { get; }
    public IReadOnlyList<BillingAccountChoice> BillingAccounts { get; protected set; } = [];
    public IReadOnlyList<OneDriveFolderChoice> Folders { get; protected set; } = [];
    public IReadOnlyList<string> DiscoveryWarnings => discoveryWarnings;
    private readonly List<string> discoveryWarnings = [];
    public bool ShowMissingBillingWarning => IsEdit && !BillingAccounts.Any(x => x.Id == Input.OriginalBillingAccountId);
    public bool ShowMissingFolderWarning => IsEdit && !string.IsNullOrWhiteSpace(Input.OriginalFolderItemId) &&
        !Folders.Any(x => x.Destination.FolderItemId == Input.OriginalFolderItemId);

    protected async Task LoadDiscoveryAsync(CancellationToken cancellationToken, bool required = true)
    {
        try
        {
            BillingAccounts = await Discovery.ListBillingAccountsAsync(Input.IntegrationType, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var message = $"Billing-account discovery failed: {ex.Message}";
            if (required) ModelState.AddModelError("Input.BillingAccountId", message);
            else discoveryWarnings.Add(message + " An unchanged stored account may be preserved.");
        }
        try
        {
            Folders = await Discovery.ListOneDriveFoldersAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var message = $"OneDrive folder discovery failed: {ex.Message}";
            if (required) ModelState.AddModelError("Input.FolderItemId", message);
            else discoveryWarnings.Add(message + " An unchanged stored folder may be preserved.");
        }
    }
}
