using InvoiceManager.AdminWeb.Services;
using InvoiceManager.Core;
using InvoiceManager.Core.Integrations.FreeAgent;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceManager.AdminWeb.Pages.Configurations;

public sealed class CreateModel(
    InvoiceConfigurationService service,
    IMicrosoftResourceDiscovery discovery,
    IFreeAgentContactDirectory contactDirectory,
    IMicrosoftAuthorizationStore authorizationStore) : ConfigurationFormPageModel(discovery, contactDirectory)
{
    [BindProperty]
    public override ConfigurationFormInput Input { get; set; } = new();
    public override bool IsEdit => false;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await CanMutateAsync()) return RedirectToPage("Index");
        // The Configuration ID field is hidden until an integration is chosen, and
        // configuration-wizard.js generates its value from GenerateSlug's JS mirror once the
        // user picks one — no server-side pre-generation needed. Billing-account discovery is
        // fetched lazily once the Microsoft billing integration is selected, so page render is
        // no longer blocked on it either.
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await CanMutateAsync()) return RedirectToPage("Index");
        await LoadDiscoveryAsync(HttpContext.RequestAborted);
        if (!ModelState.IsValid) return Page();
        var folder = await ResolveFolderAsync(storedFolder: null, HttpContext.RequestAborted);
        if (folder is null)
        {
            ModelState.AddModelError(string.Empty, "Select a OneDrive folder returned by the picker.");
            return Page();
        }
        InvoiceConfiguration configuration;
        try
        {
            configuration = Input.Build(false, BillingAccounts, currentBillingAccountId: null, folder);
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }

        var result = await service.CreateAsync(configuration, User.ToConfigurationActor(), HttpContext.RequestAborted);
        if (InvoiceConfigurationMutationErrorMessages.TryGetMessage(result) is string message)
        {
            ModelState.AddModelError(string.Empty, message);
            return Page();
        }
        TempData["StatusMessage"] = "Inactive configuration draft created.";
        return RedirectToPage("Index");
    }

    protected override Task<bool> CanMutateAsync() =>
        authorizationStore.HasTokenCacheAsync(HttpContext.RequestAborted);
}
