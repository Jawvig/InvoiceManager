using InvoiceManager.AdminWeb.Pages;
using InvoiceManager.AdminWeb.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InvoiceManager.AdminWeb.Tests;

// The "Generate expected records" trigger moved here from the Authorization page (see
// AdminAuthorizationPageTests) since it's an invoice-workflow action, not an authorization one.
public sealed class IndexPageTests
{
    [Fact]
    public async Task GenerateExpectedRecords_TriggersFunction_AndSurfacesResultAsStatusMessage()
    {
        var trigger = new FakeExpectedRecordGenerationTrigger(
            new ExpectedRecordGenerationTriggered(207));
        var model = CreateIndexModel(trigger);

        var result = await model.OnPostGenerateExpectedRecordsAsync();

        Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToPageResult>(result);
        Assert.True(trigger.WasTriggered);
        Assert.Equal(
            "Expected record generation was triggered (HTTP 207).",
            model.TempData["StatusMessage"]);
    }

    [Fact]
    public async Task GenerateExpectedRecords_ReportsMissingConfiguration_WhenFunctionsUrlIsNotConfigured()
    {
        var trigger = new FakeExpectedRecordGenerationTrigger(
            new ExpectedRecordGenerationNotConfigured());
        var model = CreateIndexModel(trigger);

        await model.OnPostGenerateExpectedRecordsAsync();

        Assert.Equal(
            "The Functions app URL is not configured, so expected record generation could not be triggered.",
            model.TempData["StatusMessage"]);
    }

    private static IndexModel CreateIndexModel(IExpectedRecordGenerationTrigger trigger)
    {
        var model = new IndexModel(trigger);

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Admin User")], "Test"))
        };
        model.PageContext = new PageContext { HttpContext = httpContext };
        model.TempData = new TempDataDictionary(httpContext, new FakeTempDataProvider());

        return model;
    }

    private sealed class FakeExpectedRecordGenerationTrigger : IExpectedRecordGenerationTrigger
    {
        private readonly ExpectedRecordGenerationTriggerResult result;

        public FakeExpectedRecordGenerationTrigger(ExpectedRecordGenerationTriggerResult? result = null)
        {
            this.result = result ?? new ExpectedRecordGenerationTriggered(207);
        }

        public bool WasTriggered { get; private set; }

        public Task<ExpectedRecordGenerationTriggerResult> TriggerAsync(CancellationToken cancellationToken)
        {
            WasTriggered = true;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context)
        {
            return new Dictionary<string, object>();
        }

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
