using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace InvoiceManager.AdminWeb.Pages;

public class ServiceStatusModel(HealthCheckService healthCheckService) : PageModel
{
    public IReadOnlyList<ServiceStatusEntry> Checks { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var report = await healthCheckService.CheckHealthAsync(HttpContext.RequestAborted);
        Checks = report.Entries
            .Select(entry => new ServiceStatusEntry(
                entry.Key,
                entry.Value.Status,
                entry.Value.Description,
                entry.Value.Exception?.Message))
            .OrderBy(entry => entry.Name)
            .ToList();
    }
}

public sealed record ServiceStatusEntry(string Name, HealthStatus Status, string? Description, string? ExceptionMessage);
