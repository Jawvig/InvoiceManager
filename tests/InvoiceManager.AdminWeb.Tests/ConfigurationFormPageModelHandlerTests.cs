using InvoiceManager.AdminWeb.Pages.Configurations;
using InvoiceManager.AdminWeb.Services;
using InvoiceManager.Core;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using InvoiceManager.TestSupport;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InvoiceManager.AdminWeb.Tests;

// Covers the AJAX discovery handlers (billing accounts, OneDrive drives/folder children) added to
// ConfigurationFormPageModel for the wizard/picker UI: unauthorized callers must be rejected, not
// served a silent empty list, and authorized callers get the discovery results as JSON.
public sealed class ConfigurationFormPageModelHandlerTests
{
    [Fact]
    public async Task OnGetBillingAccountsAsync_RejectsUnauthorizedCaller()
    {
        var model = CreateModel(hasTokenCache: false);

        var result = await model.OnGetBillingAccountsAsync(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task OnGetBillingAccountsAsync_ReturnsDiscoveredAccounts_WhenAuthorized()
    {
        var accounts = new[] { new BillingAccountChoice("acct-1", "Account One", "Business") };
        var model = CreateModel(hasTokenCache: true, billingAccounts: accounts);

        var result = await model.OnGetBillingAccountsAsync(CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        Assert.Same(accounts, json.Value);
    }

    [Fact]
    public async Task OnGetOneDriveDrivesAsync_RejectsUnauthorizedCaller()
    {
        var model = CreateModel(hasTokenCache: false);

        var result = await model.OnGetOneDriveDrivesAsync(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task OnGetOneDriveDrivesAsync_ReturnsDiscoveredDrives_WhenAuthorized()
    {
        var drives = new[] { new OneDriveDriveChoice("drive-1", "Company OneDrive") };
        var model = CreateModel(hasTokenCache: true, drives: drives);

        var result = await model.OnGetOneDriveDrivesAsync(CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        Assert.Same(drives, json.Value);
    }

    [Fact]
    public async Task OnGetOneDriveFolderChildrenAsync_RejectsUnauthorizedCaller()
    {
        var model = CreateModel(hasTokenCache: false);

        var result = await model.OnGetOneDriveFolderChildrenAsync("drive-1", null, CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task OnGetOneDriveFolderChildrenAsync_ReturnsDiscoveredFolders_WhenAuthorized()
    {
        var folders = new[] { new OneDriveFolderEntry("folder-1", "Bills") };
        var model = CreateModel(hasTokenCache: true, folderChildren: folders);

        var result = await model.OnGetOneDriveFolderChildrenAsync("drive-1", "parent-1", CancellationToken.None);

        var json = Assert.IsType<JsonResult>(result);
        Assert.Same(folders, json.Value);
    }

    [Fact]
    public async Task OnGetOneDriveFolderChildrenAsync_RejectsMissingDriveId()
    {
        var model = CreateModel(hasTokenCache: true);

        var result = await model.OnGetOneDriveFolderChildrenAsync("", null, CancellationToken.None);

        Assert.IsType<BadRequestResult>(result);
    }

    [Fact]
    public async Task OnGetFreeAgentContactsAsync_RejectsUnauthorizedCaller()
    {
        var model = CreateModel(hasTokenCache: false);

        var result = await model.OnGetFreeAgentContactsAsync("acme", CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task OnGetFreeAgentContactsAsync_RejectsQueryShorterThanThreeCharacters()
    {
        var model = CreateModel(hasTokenCache: true);

        var result = await model.OnGetFreeAgentContactsAsync("ac", CancellationToken.None);

        Assert.IsType<BadRequestResult>(result);
    }

    [Fact]
    public async Task OnGetFreeAgentContactsAsync_ReturnsSearchResults_WhenAuthorized()
    {
        var contactDirectory = new FakeFreeAgentContactDirectory
        {
            SearchResults = [new InvoiceManager.Core.Integrations.FreeAgent.FreeAgentContact(
                new InvoiceManager.Core.Integrations.FreeAgent.FreeAgentContactIdentity("https://api.sandbox.freeagent.com/v2/contacts/1"),
                "Acme Widgets Ltd")],
        };
        var model = CreateModel(hasTokenCache: true, contactDirectory: contactDirectory);

        var result = await model.OnGetFreeAgentContactsAsync("acme", CancellationToken.None);

        Assert.IsType<JsonResult>(result);
        Assert.Equal(["acme"], contactDirectory.SearchQueries);
    }

    [Fact]
    public async Task OnPostAsync_RevalidatesFreeAgentContact_EvenThoughThePickerAlreadyVerifiedIt()
    {
        // The posted URL/display name are hidden inputs a forged request could set directly, and
        // even a genuine picker selection can go stale between search and submission - so Create
        // must re-confirm the contact on save exactly like Edit/Import, not trust the picker
        // outright.
        var contactDirectory = new FakeFreeAgentContactDirectory
        {
            GetResult = new InvoiceManager.Core.Integrations.FreeAgent.FreeAgentContact(
                new InvoiceManager.Core.Integrations.FreeAgent.FreeAgentContactIdentity("https://api.sandbox.freeagent.com/v2/contacts/1"),
                "Acme Widgets Ltd"),
        };
        var model = CreateModel(
            hasTokenCache: true,
            verifiedFolder: new OneDriveFolder("drive-1", "Drive", "folder-1", "/Bills"),
            contactDirectory: contactDirectory);
        model.Input = new ConfigurationFormInput
        {
            Id = "email-invoice",
            IntegrationType = IntegrationType.GraphEmail,
            SenderEmailAddress = "billing@example.com",
            BodyPattern = "Invoice \\d+",
            HasFreeAgentMatching = true,
            FreeAgentContactUrl = "https://api.sandbox.freeagent.com/v2/contacts/1",
            FreeAgentContactDisplayName = "Acme Widgets Ltd",
            DriveId = "drive-1",
            DriveName = "Drive",
            FolderItemId = "folder-1",
            FolderPath = "/Bills",
        };

        var result = await model.OnPostAsync();

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Single(contactDirectory.GetRequests);
    }

    [Fact]
    public async Task OnPostAsync_RejectsCreate_WhenFreeAgentContactDoesNotResolve()
    {
        var contactDirectory = new FakeFreeAgentContactDirectory { GetResult = Option.None };
        var model = CreateModel(
            hasTokenCache: true,
            verifiedFolder: new OneDriveFolder("drive-1", "Drive", "folder-1", "/Bills"),
            contactDirectory: contactDirectory);
        model.Input = new ConfigurationFormInput
        {
            Id = "email-invoice",
            IntegrationType = IntegrationType.GraphEmail,
            SenderEmailAddress = "billing@example.com",
            BodyPattern = "Invoice \\d+",
            HasFreeAgentMatching = true,
            FreeAgentContactUrl = "https://api.sandbox.freeagent.com/v2/contacts/1",
            FreeAgentContactDisplayName = "Acme Widgets Ltd",
            DriveId = "drive-1",
            DriveName = "Drive",
            FolderItemId = "folder-1",
            FolderPath = "/Bills",
        };

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
    }

    private static CreateModel CreateModel(
        bool hasTokenCache,
        IReadOnlyList<BillingAccountChoice>? billingAccounts = null,
        IReadOnlyList<OneDriveDriveChoice>? drives = null,
        IReadOnlyList<OneDriveFolderEntry>? folderChildren = null,
        OneDriveFolder? verifiedFolder = null,
        FakeFreeAgentContactDirectory? contactDirectory = null)
    {
        var discovery = new FakeMicrosoftResourceDiscovery(billingAccounts, drives, folderChildren, verifiedFolder);
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("oid", "11111111-1111-1111-1111-111111111111"), new Claim(ClaimTypes.Name, "Admin User")],
                "Test")),
        };
        var model = new CreateModel(
            new InvoiceConfigurationService(new FakeConfigurationRepository()),
            discovery,
            contactDirectory ?? new FakeFreeAgentContactDirectory(),
            new FakeMicrosoftAuthorizationStore(hasTokenCache))
        {
            PageContext = new PageContext { HttpContext = httpContext },
            TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(httpContext, new NoopTempDataProvider()),
        };
        return model;
    }

    private sealed class NoopTempDataProvider : Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
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
}
