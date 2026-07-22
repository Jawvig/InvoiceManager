using InvoiceManager.AdminWeb.Services;
using InvoiceManager.Core;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InvoiceManager.AdminWeb.Pages.Configurations;

public sealed class IndexModel(
    InvoiceConfigurationService service,
    IMicrosoftAuthorizationStore authorizationStore,
    IMicrosoftResourceDiscovery discovery) : PageModel
{
    public IReadOnlyList<StoredInvoiceConfiguration> Configurations { get; private set; } = [];
    public bool HasWorkflowAuthorization { get; private set; }
    public string? StatusMessage { get; private set; }
    public bool EditingEnabled => HasWorkflowAuthorization;

    // Billing account id -> display name only (the ID itself is shown separately, behind a
    // "Show ID" disclosure, rather than baked into this label). Best-effort: falls back to
    // displaying the raw ID (via GetValueOrDefault in the view) if discovery fails or an
    // account isn't found.
    public IReadOnlyDictionary<string, string> BillingAccountLabels { get; private set; } =
        new Dictionary<string, string>();

    public async Task OnGetAsync()
    {
        await LoadAsync();
        StatusMessage = TempData["StatusMessage"] as string;
    }

    public async Task<IActionResult> OnPostSetActiveAsync(
        string id,
        IntegrationType integrationType,
        string etag,
        bool activate)
    {
        if (!await authorizationStore.HasTokenCacheAsync(HttpContext.RequestAborted))
        {
            TempData["StatusMessage"] = "Capture workflow authorization before changing configuration state.";
            return RedirectToPage();
        }
        try
        {
            var current = await service.GetAsync(new(id), integrationType, HttpContext.RequestAborted);
            if (current is not StoredInvoiceConfiguration stored)
                return NotFound();
            await service.SetActiveAsync(
                stored with { ETag = etag }, activate, User.ToConfigurationActor(), HttpContext.RequestAborted);
            TempData["StatusMessage"] = activate
                ? "Configuration activated. Processing will occur on the next scheduled or manual workflow run."
                : "Configuration deactivated. Outstanding records are preserved and will be skipped while it is inactive.";
        }
        catch (InvoiceConfigurationConflictException ex)
        {
            TempData["StatusMessage"] = ex.Message;
        }
        return RedirectToPage();
    }

    // MicrosoftResourceDiscovery.ListBillingAccountsAsync builds Label as "DisplayName (id)"
    // (or just "id" when there's no display name) for use in the Edit form's account picker,
    // where showing the id disambiguates same-named accounts. This list instead shows the id
    // separately behind a "Show ID" disclosure, so strip the "(id)" suffix back off here.
    private static string StripTrailingId(string label, string id)
    {
        var suffix = $" ({id})";
        return label.EndsWith(suffix, StringComparison.Ordinal)
            ? label[..^suffix.Length]
            : label;
    }

    private async Task LoadAsync()
    {
        Configurations = await service.ListAsync(HttpContext.RequestAborted);
        HasWorkflowAuthorization = await authorizationStore.HasTokenCacheAsync(HttpContext.RequestAborted);

        if (Configurations.Any(c => c.Configuration.IntegrationType == IntegrationType.MicrosoftBilling))
        {
            try
            {
                var accounts = await discovery.ListBillingAccountsAsync(HttpContext.RequestAborted);
                BillingAccountLabels = accounts.ToDictionary(a => a.Id, a => StripTrailingId(a.Label, a.Id));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Best-effort only: the list still renders with raw billing account IDs.
            }
        }
    }
}
