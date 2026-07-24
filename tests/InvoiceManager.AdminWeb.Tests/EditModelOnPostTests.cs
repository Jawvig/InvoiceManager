using InvoiceManager.AdminWeb.Pages.Configurations;
using InvoiceManager.AdminWeb.Services;
using InvoiceManager.Core;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using InvoiceManager.TestSupport;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace InvoiceManager.AdminWeb.Tests;

// Covers the Edit page's trust boundary for the billing account and OneDrive folder selections:
// both are posted as hidden fields a forged request can set directly, so acceptance must be
// decided against server-loaded state (the stored configuration, and a live Graph lookup),
// never against another posted field or an unverified posted value.
public sealed class EditModelOnPostTests
{
    private static readonly OneDriveFolder StoredFolder = new("drive-id", "Drive", "folder-id", "/Bills");
    private static readonly InvoiceConfiguration StoredConfiguration = new(
        new InvoiceConfigurationId("billing-invoice"),
        new MicrosoftBillingIntegrationConfiguration("stored-billing-id"),
        "Stored invoice",
        InvoiceFrequency.Monthly,
        Option.None,
        VatMode.Exclusive,
        IsActive: false,
        StoredFolder,
        DateOnly.FromDateTime(DateTime.UtcNow),
        DateToleranceDays: 5);

    [Fact]
    public async Task OnPostAsync_RejectsForgedBillingAccount_EvenWhenPostedOriginalMatches()
    {
        // Discovery returns no accounts (e.g. a stale/failed fetch), and the forged request sets
        // both BillingAccountId and OriginalBillingAccountId to the same made-up value rather
        // than the actual stored "stored-billing-id" — this must not be accepted.
        var model = CreateModel(billingAccounts: []);
        model.Input = ConfigurationFormInput.From(new StoredInvoiceConfiguration(StoredConfiguration, "etag-billing-invoice"));
        model.Input.BillingAccountId = "forged-id";
        model.Input.OriginalBillingAccountId = "forged-id";

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
    }

    [Fact]
    public async Task OnPostAsync_RejectsForgedOneDriveFolder_WhenGraphDoesNotVerifyIt()
    {
        var model = CreateModel(
            billingAccounts: [new BillingAccountChoice("stored-billing-id", "Account", "Business")],
            verifiedFolder: null);
        model.Input = ConfigurationFormInput.From(new StoredInvoiceConfiguration(StoredConfiguration, "etag-billing-invoice"));
        model.Input.FolderItemId = "forged-folder-id";

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
    }

    [Fact]
    public async Task OnPostAsync_AcceptsUnchangedSelections_WithoutRequiringDiscovery()
    {
        // The common case: nothing about the billing account or folder changed. This must
        // succeed even with an empty discovery list and no Graph verification call needed.
        var model = CreateModel(billingAccounts: []);
        model.Input = ConfigurationFormInput.From(new StoredInvoiceConfiguration(StoredConfiguration, "etag-billing-invoice"));

        var result = await model.OnPostAsync();

        Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToPageResult>(result);
    }

    private static EditModel CreateModel(
        IReadOnlyList<BillingAccountChoice>? billingAccounts = null,
        OneDriveFolder? verifiedFolder = null)
    {
        var discovery = new FakeMicrosoftResourceDiscovery(billingAccounts, verifiedFolder: verifiedFolder);
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("oid", "11111111-1111-1111-1111-111111111111"), new Claim(ClaimTypes.Name, "Admin User")],
                "Test")),
        };
        var model = new EditModel(
            new InvoiceConfigurationService(new FakeConfigurationRepository(StoredConfiguration)),
            discovery,
            new FakeMicrosoftAuthorizationStore(hasTokenCache: true))
        {
            PageContext = new PageContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new NoopTempDataProvider()),
        };
        return model;
    }

    private sealed class FakeMicrosoftAuthorizationStore(bool hasTokenCache) : IMicrosoftAuthorizationStore
    {
        public Task<bool> HasTokenCacheAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(hasTokenCache);

        public Task<byte[]?> ReadTokenCacheAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(null);

        public Task SaveTokenCacheAsync(byte[] tokenCache, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ClearTokenCacheAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
