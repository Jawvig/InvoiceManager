using System.Text.Json;
using InvoiceManager.AdminWeb.Services;
using InvoiceManager.Core;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InvoiceManager.AdminWeb.Pages.Configurations;

public sealed record RevisionDisplay(InvoiceConfigurationRevision Revision, string Diff, string SnapshotJson);

public sealed class HistoryModel(
    InvoiceConfigurationService service,
    IMicrosoftAuthorizationStore authorizationStore) : PageModel
{
    public StoredInvoiceConfiguration? Current { get; private set; }
    public IReadOnlyList<RevisionDisplay> Revisions { get; private set; } = [];
    public bool CanRestore { get; private set; }
    public string? StatusMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(string id, IntegrationType integrationType)
    {
        if (!await LoadAsync(id, integrationType)) return NotFound();
        StatusMessage = TempData["StatusMessage"] as string;
        return Page();
    }

    public async Task<IActionResult> OnPostRestoreAsync(
        string id,
        IntegrationType integrationType,
        string revisionId,
        string etag,
        bool confirmed)
    {
        if (!confirmed)
        {
            TempData["StatusMessage"] = "Confirm the revision restore before continuing.";
            return RedirectToPage(new { id, integrationType });
        }
        if (!await authorizationStore.HasTokenCacheAsync(HttpContext.RequestAborted))
        {
            TempData["StatusMessage"] = "Workflow authorization is required before restore.";
            return RedirectToPage(new { id, integrationType });
        }

        try
        {
            var currentResult = await service.GetAsync(new(id), integrationType, HttpContext.RequestAborted);
            if (currentResult is not StoredInvoiceConfiguration current) return NotFound();
            var revisions = await service.ListRevisionsAsync(new(id), integrationType, HttpContext.RequestAborted);
            var revision = revisions.SingleOrDefault(x => x.RevisionId == revisionId);
            if (revision is null) return NotFound();
            await service.RestoreAsync(
                current with { ETag = etag }, revision, User.ToConfigurationActor(), HttpContext.RequestAborted);
            TempData["StatusMessage"] = "Revision restored as a new audited revision; activation state was unchanged.";
        }
        catch (InvoiceConfigurationConflictException ex)
        {
            TempData["StatusMessage"] = ex.Message;
        }
        return RedirectToPage(new { id, integrationType });
    }

    private async Task<bool> LoadAsync(string id, IntegrationType integrationType)
    {
        var current = await service.GetAsync(new(id), integrationType, HttpContext.RequestAborted);
        if (current is not StoredInvoiceConfiguration stored) return false;
        Current = stored;
        var revisions = await service.ListRevisionsAsync(new(id), integrationType, HttpContext.RequestAborted);
        var displayed = new List<RevisionDisplay>();
        InvoiceConfiguration? prior = null;
        foreach (var revision in revisions.OrderByDescending(x => x.Timestamp))
        {
            // Find the immediately preceding chronological snapshot for a compact field diff.
            var chronological = revisions.OrderBy(x => x.Timestamp).ToList();
            var index = chronological.FindIndex(x => x.RevisionId == revision.RevisionId);
            prior = index > 0 ? chronological[index - 1].Snapshot : null;
            displayed.Add(new(
                revision,
                Diff(prior, revision.Snapshot),
                JsonSerializer.Serialize(revision.Snapshot, new JsonSerializerOptions { WriteIndented = true })));
        }
        Revisions = displayed;
        CanRestore = await authorizationStore.HasTokenCacheAsync(HttpContext.RequestAborted);
        return true;
    }

    private static string Diff(InvoiceConfiguration? prior, InvoiceConfiguration current)
    {
        if (prior is null) return "Initial snapshot";
        var changes = new List<string>();
        Add("description", prior.InvoiceDescription, current.InvoiceDescription);
        Add("frequency", prior.Frequency, current.Frequency);
        Add("amount criteria", prior.AmountMatchingCriteria, current.AmountMatchingCriteria);
        Add("VAT mode", prior.DefaultVatMode, current.DefaultVatMode);
        Add("status", prior.IsActive ? "active" : "inactive", current.IsActive ? "active" : "inactive");
        Add("OneDrive folder", prior.OneDriveFolder.FolderPath, current.OneDriveFolder.FolderPath);
        Add("start date", prior.StartDate, current.StartDate);
        Add("integration configuration", prior.IntegrationConfiguration, current.IntegrationConfiguration);
        Add("date tolerance", prior.DateToleranceDays, current.DateToleranceDays);
        return changes.Count == 0 ? "No business-field changes" : string.Join("; ", changes);

        void Add(string name, object? before, object? after)
        {
            if (!Equals(before, after)) changes.Add($"{name}: {before} → {after}");
        }
    }
}
