using InvoiceManager.AdminWeb.Pages.Configurations;
using InvoiceManager.AdminWeb.Services;
using InvoiceManager.Core;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using InvoiceManager.TestSupport;
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

    private static CreateModel CreateModel(
        bool hasTokenCache,
        IReadOnlyList<BillingAccountChoice>? billingAccounts = null,
        IReadOnlyList<OneDriveDriveChoice>? drives = null,
        IReadOnlyList<OneDriveFolderEntry>? folderChildren = null)
    {
        var discovery = new FakeMicrosoftResourceDiscovery(billingAccounts, drives, folderChildren);
        var model = new CreateModel(
            new InvoiceConfigurationService(new FakeConfigurationRepository()),
            discovery,
            new FakeMicrosoftAuthorizationStore(hasTokenCache))
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
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
}
