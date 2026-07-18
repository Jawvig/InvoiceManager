using InvoiceManager.AdminWeb.Services;
using InvoiceManager.Core;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceManager.AdminWeb.Pages.Configurations;

public sealed class EditModel(
    InvoiceConfigurationService service,
    IMicrosoftResourceDiscovery discovery,
    IMicrosoftAuthorizationStore authorizationStore,
    LegacyInvoiceRecordMigration migration) : ConfigurationFormPageModel(discovery)
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

        if (stored.Configuration.OneDriveDestination.IsLegacyPath)
        {
            var resolved = await Discovery.ResolveLegacyOneDrivePathAsync(
                stored.Configuration.OneDriveDestination.DisplayPath, HttpContext.RequestAborted);
            if (resolved is OneDriveDestination destination)
            {
                Input.FolderItemId = Input.OriginalFolderItemId = destination.FolderItemId!;
                Input.OriginalDriveId = destination.DriveId!;
                Input.OriginalDisplayPath = destination.DisplayPath;
            }
            else
            {
                ModelState.AddModelError("Input.FolderItemId",
                    "The legacy OneDrive path could not be resolved. Restore access to that folder before editing.");
            }
        }
        await LoadDiscoveryAsync(HttpContext.RequestAborted, required: false);
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
                current.Configuration.IsActive, BillingAccounts, Folders, true, current.Configuration);
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

    private async Task<bool> CanMutateAsync() =>
        await authorizationStore.HasTokenCacheAsync(HttpContext.RequestAborted) &&
        await migration.CountPendingAsync(HttpContext.RequestAborted) == 0;
}
