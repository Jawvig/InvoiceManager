using InvoiceManager.AdminWeb.Services;
using InvoiceManager.Core;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceManager.AdminWeb.Pages.Configurations;

public sealed class EditModel(
    InvoiceConfigurationService service,
    IMicrosoftResourceDiscovery discovery,
    IMicrosoftAuthorizationStore authorizationStore) : ConfigurationFormPageModel(discovery)
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
        var currentResult = await service.GetAsync(new(Input.Id), Input.IntegrationType, HttpContext.RequestAborted);
        if (currentResult is not StoredInvoiceConfiguration current) return NotFound();
        if (!ModelState.IsValid) return Page();

        try
        {
            var updated = Input.Build(
                current.Configuration.IsActive, BillingAccounts, true);
            await service.UpdateAsync(
                current.Configuration, updated, Input.ETag, User.ToConfigurationActor(), HttpContext.RequestAborted);
            TempData["StatusMessage"] = "Configuration updated. Existing expected records retain their snapshots.";
            return RedirectToPage("Index");
        }
        catch (InvoiceConfigurationConflictException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
        }
        return Page();
    }

    protected override Task<bool> CanMutateAsync() =>
        authorizationStore.HasTokenCacheAsync(HttpContext.RequestAborted);
}
