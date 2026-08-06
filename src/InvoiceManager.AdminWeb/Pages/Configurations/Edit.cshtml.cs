using InvoiceManager.AdminWeb.Services;
using InvoiceManager.Core;
using InvoiceManager.Core.Integrations.FreeAgent;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceManager.AdminWeb.Pages.Configurations;

public sealed class EditModel(
    InvoiceConfigurationService service,
    IMicrosoftResourceDiscovery discovery,
    IFreeAgentContactDirectory contactDirectory,
    IMicrosoftAuthorizationStore authorizationStore) : ConfigurationFormPageModel(discovery, contactDirectory)
{
    [BindProperty]
    public override ConfigurationFormInput Input { get; set; } = new();
    public override bool IsEdit => true;

    public async Task<IActionResult> OnGetAsync(string id, IntegrationType integrationType)
    {
        if (!await CanMutateAsync()) return RedirectToPage("Index");
        var current = await service.GetAsync(new(id), integrationType, HttpContext.RequestAborted);
        if (current is not StoredInvoiceConfiguration stored) return NotFound();
        Input = ConfigurationFormInput.From(stored);

        // Render immediately with the stored billing-account value pre-populated (see
        // ConfigurationFormInput.From); the full list is fetched in the background by
        // configuration-wizard.js via OnGetBillingAccountsAsync rather than blocking here.
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await CanMutateAsync()) return RedirectToPage("Index");
        await LoadDiscoveryAsync(HttpContext.RequestAborted, required: false);
        // Edit always posts IntegrationType via a hidden input (see _ConfigurationForm.cshtml),
        // so it should never actually be null here — but the property is nullable (Create's
        // "please choose" placeholder), so guard defensively rather than assume.
        if (Input.IntegrationType is null) return Page();
        var currentResult = await service.GetAsync(new(Input.Id), Input.IntegrationType.Value, HttpContext.RequestAborted);
        if (currentResult is not StoredInvoiceConfiguration current) return NotFound();
        if (!await RefreshFreeAgentContactAsync(HttpContext.RequestAborted)) return Page();
        if (!ModelState.IsValid) return Page();

        var folder = await ResolveFolderAsync(current.Configuration.OneDriveFolder, HttpContext.RequestAborted);
        if (folder is null)
        {
            ModelState.AddModelError(string.Empty, "Select a OneDrive folder returned by the picker.");
            return Page();
        }
        var currentBillingAccountId =
            current.Configuration.IntegrationConfiguration is MicrosoftBillingIntegrationConfiguration billing
                ? billing.BillingAccountId
                : null;

        InvoiceConfiguration updated;
        try
        {
            updated = Input.Build(current.Configuration.IsActive, BillingAccounts, currentBillingAccountId, folder);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }

        var result = await service.UpdateAsync(
            current.Configuration, updated, Input.ETag, User.ToConfigurationActor(), HttpContext.RequestAborted);
        if (InvoiceConfigurationMutationErrorMessages.TryGetMessage(result) is string message)
        {
            ModelState.AddModelError(string.Empty, message);
            return Page();
        }
        TempData["StatusMessage"] = "Configuration updated. Existing expected records retain their snapshots.";
        return RedirectToPage("Index");
    }

    protected override Task<bool> CanMutateAsync() =>
        authorizationStore.HasTokenCacheAsync(HttpContext.RequestAborted);
}
