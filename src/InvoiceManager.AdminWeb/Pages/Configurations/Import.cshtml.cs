using System.Text.Json;
using InvoiceManager.AdminWeb.Services;
using InvoiceManager.Core;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceManager.AdminWeb.Pages.Configurations;

public sealed class ImportModel(
    InvoiceConfigurationService service,
    IMicrosoftResourceDiscovery discovery,
    IMicrosoftAuthorizationStore authorizationStore) : ConfigurationFormPageModel(discovery)
{
    [BindProperty]
    public override ConfigurationFormInput Input { get; set; } = new();
    public override bool IsEdit => false;

    // Distinguishes the initial "choose a file" step from the pre-filled review/save step: both
    // are served by this same page so the review step can reuse _ConfigurationForm.cshtml as-is.
    public bool HasImportedFile { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await CanMutateAsync()) return RedirectToPage("Index");
        return Page();
    }

    public async Task<IActionResult> OnPostUploadAsync(IFormFile? file)
    {
        if (!await CanMutateAsync()) return RedirectToPage("Index");
        // The upload form only posts a file, so binding Input against its untouched defaults
        // (blank Id, no IntegrationType, ...) produces spurious "required" errors that have
        // nothing to do with this handler — clear them before adding any real error of our own.
        ModelState.Clear();

        if (file is null || file.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "Choose a configuration export file to import.");
            return Page();
        }

        InvoiceConfigurationExport export;
        try
        {
            await using var stream = file.OpenReadStream();
            export = await JsonSerializer.DeserializeAsync<InvoiceConfigurationExport>(
                stream, InvoiceConfigurationExportJson.Options, HttpContext.RequestAborted)
                ?? throw new JsonException("The file does not contain a configuration.");
        }
        catch (JsonException ex)
        {
            ModelState.AddModelError(string.Empty, $"That file isn't a valid configuration export: {ex.Message}");
            return Page();
        }

        Input = export.ToFormInput();
        HasImportedFile = true;
        return Page();
    }

    // The save step: identical in shape to CreateModel.OnPostAsync (an import always produces a
    // new, inactive configuration) — the OneDrive folder and billing account fields the user
    // reviewed on this page still go through the same folder-resolution / discovery-list checks
    // a manually entered configuration would, so nothing from the imported file is trusted
    // outright.
    public async Task<IActionResult> OnPostAsync()
    {
        if (!await CanMutateAsync()) return RedirectToPage("Index");
        HasImportedFile = true;
        await LoadDiscoveryAsync(HttpContext.RequestAborted);
        if (!ModelState.IsValid) return Page();
        var folder = await ResolveFolderAsync(storedFolder: null, HttpContext.RequestAborted);
        if (folder is null)
        {
            ModelState.AddModelError(string.Empty, "Select a OneDrive folder returned by the picker.");
            return Page();
        }
        try
        {
            var configuration = Input.Build(false, BillingAccounts, currentBillingAccountId: null, folder);
            await service.CreateAsync(configuration, User.ToConfigurationActor(), HttpContext.RequestAborted);
            TempData["StatusMessage"] = "Inactive configuration draft imported.";
            return RedirectToPage("Index");
        }
        catch (Exception ex) when (ex is ArgumentException or DuplicateInvoiceConfigurationException)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }

    protected override Task<bool> CanMutateAsync() =>
        authorizationStore.HasTokenCacheAsync(HttpContext.RequestAborted);
}
