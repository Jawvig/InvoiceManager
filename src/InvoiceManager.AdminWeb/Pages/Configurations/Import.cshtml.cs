using System.Text.Json;
using InvoiceManager.AdminWeb.Services;
using InvoiceManager.Core;
using InvoiceManager.Core.Integrations.FreeAgent;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceManager.AdminWeb.Pages.Configurations;

public sealed class ImportModel(
    InvoiceConfigurationService service,
    IMicrosoftResourceDiscovery discovery,
    IFreeAgentContactDirectory contactDirectory,
    IMicrosoftAuthorizationStore authorizationStore) : ConfigurationFormPageModel(discovery, contactDirectory)
{
    [BindProperty]
    public override ConfigurationFormInput Input { get; set; } = new();
    public override bool IsEdit => false;

    // Distinguishes the initial "choose a file" step from the pre-filled review/save step: both
    // are served by this same page so the review step can reuse _ConfigurationForm.cshtml as-is.
    public bool HasImportedFile { get; private set; }

    public override bool RequiresImportConfirmation => HasImportedFile;

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

        try
        {
            await using var stream = file.OpenReadStream();
            var export = await JsonSerializer.DeserializeAsync<InvoiceConfigurationExport>(
                stream, InvoiceConfigurationExportJson.Options, HttpContext.RequestAborted)
                ?? throw new JsonException("The file does not contain a configuration.");
            // ToFormInput() validates the deserialized shape (e.g. an explicit JSON null for the
            // OneDrive folder, or an enum value with no matching named member) and throws
            // JsonException too, so it belongs inside this same try — otherwise an invalid but
            // parsable file would crash instead of showing the message below.
            Input = export.ToFormInput();
        }
        catch (JsonException ex)
        {
            ModelState.AddModelError(string.Empty, $"That file isn't a valid configuration export: {ex.Message}");
            return Page();
        }

        Input.Id = await UniqueIdAsync(Input.Id, HttpContext.RequestAborted);
        HasImportedFile = true;
        return Page();
    }

    // Rather than reject an imported file outright when its Configuration ID is already taken in
    // this environment, append "-1" (or "-2", ...) so the review/save step below doesn't start
    // out already failing validation - the user can still rename it before saving if they'd
    // rather use a different ID.
    private async Task<string> UniqueIdAsync(string id, CancellationToken cancellationToken)
    {
        var existingIds = (await service.ListAsync(cancellationToken))
            .Select(x => x.Configuration.Id.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existingIds.Contains(id)) return id;

        var suffix = 1;
        string candidate;
        do
        {
            candidate = $"{id}-{suffix}";
            suffix++;
        } while (existingIds.Contains(candidate));
        return candidate;
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
        if (!ConfirmedFolderSelection)
        {
            ModelState.AddModelError(string.Empty, "Confirm the OneDrive folder is correct for this environment before saving.");
        }
        if (Input.IntegrationType == IntegrationType.MicrosoftBilling && !ConfirmedBillingAccountSelection)
        {
            ModelState.AddModelError(string.Empty, "Confirm the billing account is correct for this environment before saving.");
        }
        await RefreshFreeAgentContactAsync(HttpContext.RequestAborted);
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
        TempData["StatusMessage"] = "Inactive configuration draft imported.";
        return RedirectToPage("Index");
    }

    protected override Task<bool> CanMutateAsync() =>
        authorizationStore.HasTokenCacheAsync(HttpContext.RequestAborted);
}
