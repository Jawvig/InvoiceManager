using InvoiceManager.AdminWeb.Services;
using InvoiceManager.Core;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InvoiceManager.AdminWeb.Pages.Configurations;

public sealed class IndexModel(
    InvoiceConfigurationService service,
    LegacyInvoiceRecordMigration migration,
    IMicrosoftAuthorizationStore authorizationStore) : PageModel
{
    public IReadOnlyList<StoredInvoiceConfiguration> Configurations { get; private set; } = [];
    public bool HasWorkflowAuthorization { get; private set; }
    public int PendingLegacyRecords { get; private set; }
    public string? StatusMessage { get; private set; }
    public bool EditingEnabled => HasWorkflowAuthorization && PendingLegacyRecords == 0;

    public async Task OnGetAsync()
    {
        await LoadAsync();
        StatusMessage = TempData["StatusMessage"] as string;
    }

    public async Task<IActionResult> OnPostMigrateAsync()
    {
        var result = await migration.RunAsync(HttpContext.RequestAborted);
        var failureText = result.Failures.Count == 0
            ? ""
            : " " + string.Join(" ", result.Failures.Select(x => $"{x.ConfigurationId}: {x.Message}"));
        TempData["StatusMessage"] =
            $"Legacy record migration: {result.Migrated} migrated, {result.Skipped} skipped, {result.Failed} failed.{failureText}";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetActiveAsync(
        string id,
        IntegrationType integrationType,
        string etag,
        bool activate,
        bool confirmed)
    {
        if (!confirmed)
        {
            TempData["StatusMessage"] = "Confirm the activation-state change before continuing.";
            return RedirectToPage();
        }
        if (!await authorizationStore.HasTokenCacheAsync(HttpContext.RequestAborted))
        {
            TempData["StatusMessage"] = "Capture workflow authorization before changing configuration state.";
            return RedirectToPage();
        }
        if (await migration.CountPendingAsync(HttpContext.RequestAborted) > 0)
        {
            TempData["StatusMessage"] = "Migrate retryable legacy records before changing configurations.";
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

    private async Task LoadAsync()
    {
        Configurations = await service.ListAsync(HttpContext.RequestAborted);
        HasWorkflowAuthorization = await authorizationStore.HasTokenCacheAsync(HttpContext.RequestAborted);
        PendingLegacyRecords = await migration.CountPendingAsync(HttpContext.RequestAborted);
    }
}
