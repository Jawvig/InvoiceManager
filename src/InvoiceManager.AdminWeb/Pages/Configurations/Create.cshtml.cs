using InvoiceManager.AdminWeb.Services;
using InvoiceManager.Core;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceManager.AdminWeb.Pages.Configurations;

public sealed class CreateModel(
    InvoiceConfigurationService service,
    IMicrosoftResourceDiscovery discovery,
    IMicrosoftAuthorizationStore authorizationStore) : ConfigurationFormPageModel(discovery)
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
            var configuration = Input.Build(false, BillingAccounts, false);
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

    private Task<bool> CanMutateAsync() =>
        authorizationStore.HasTokenCacheAsync(HttpContext.RequestAborted);
}
