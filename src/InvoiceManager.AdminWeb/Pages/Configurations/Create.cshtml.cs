using InvoiceManager.AdminWeb.Services;
using InvoiceManager.Core;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceManager.AdminWeb.Pages.Configurations;

public sealed class CreateModel(
    InvoiceConfigurationService service,
    IMicrosoftResourceDiscovery discovery,
    IMicrosoftAuthorizationStore authorizationStore,
    LegacyInvoiceRecordMigration migration) : ConfigurationFormPageModel(discovery)
{
    [BindProperty]
    public override ConfigurationFormInput Input { get; set; } = new();
    public override bool IsEdit => false;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await CanMutateAsync()) return RedirectToPage("Index");
        Input.Id = InvoiceConfigurationValidation.GenerateSlug(null, Input.IntegrationType);
        await LoadDiscoveryAsync(HttpContext.RequestAborted);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await CanMutateAsync()) return RedirectToPage("Index");
        await LoadDiscoveryAsync(HttpContext.RequestAborted);
        if (!ModelState.IsValid) return Page();
        try
        {
            if (Input.IntegrationType is not (IntegrationType.Microsoft365 or IntegrationType.Azure))
                throw new ArgumentException("Only Microsoft365 and Azure configurations can currently be created.");
            var configuration = Input.Build(false, BillingAccounts, Folders, false);
            await service.CreateAsync(configuration, User.ToConfigurationActor(), HttpContext.RequestAborted);
            TempData["StatusMessage"] = "Inactive configuration draft created.";
            return RedirectToPage("Index");
        }
        catch (Exception ex) when (ex is ArgumentException or DuplicateInvoiceConfigurationException)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }

    private async Task<bool> CanMutateAsync() =>
        await authorizationStore.HasTokenCacheAsync(HttpContext.RequestAborted) &&
        await migration.CountPendingAsync(HttpContext.RequestAborted) == 0;
}
