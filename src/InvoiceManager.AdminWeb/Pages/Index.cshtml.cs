using InvoiceManager.AdminWeb.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InvoiceManager.AdminWeb.Pages;

public class IndexModel(IExpectedRecordGenerationTrigger expectedRecordGenerationTrigger) : PageModel
{
    public string? StatusMessage { get; private set; }

    public void OnGet()
    {
        StatusMessage = TempData["StatusMessage"] as string;
    }

    public async Task<IActionResult> OnPostGenerateExpectedRecordsAsync()
    {
        var result = await expectedRecordGenerationTrigger.TriggerAsync(HttpContext.RequestAborted);
        TempData["StatusMessage"] = result switch
        {
            ExpectedRecordGenerationTriggered triggered =>
                $"Expected record generation was triggered (HTTP {triggered.StatusCode}).",
            ExpectedRecordGenerationNotConfigured =>
                "The Functions app URL is not configured, so expected record generation could not be triggered.",
            ExpectedRecordGenerationFailed failed =>
                $"Expected record generation could not be triggered. {failed.Message}",
        };
        return RedirectToPage();
    }
}
