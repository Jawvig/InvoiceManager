using System.Security.Claims;
using System.Text;
using InvoiceManager.AdminWeb.Pages.Configurations;
using InvoiceManager.AdminWeb.Services;
using InvoiceManager.Core;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using InvoiceManager.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace InvoiceManager.AdminWeb.Tests;

// Covers the Import page's two-step flow: parsing an uploaded export file into a pre-filled
// Input (OnPostUploadAsync), and saving it through the same folder-resolution / discovery-list
// checks a manually entered Create submission goes through (OnPostAsync) - nothing from the file
// is trusted without being re-verified.
public sealed class ImportModelTests
{
    private const string ValidExportJson = """
        {
          "id": "imported-config",
          "integrationType": "MicrosoftBilling",
          "billingAccountId": "billing-account-1",
          "invoiceDescription": "Imported invoice",
          "frequency": "Monthly",
          "defaultVatMode": "Exclusive",
          "oneDriveFolder": {
            "driveId": "drive-1",
            "driveName": "Drive One",
            "folderItemId": "folder-1",
            "folderPath": "/Bills/Imported"
          },
          "startDate": "2025-01-01",
          "dateToleranceDays": 5
        }
        """;

    [Fact]
    public async Task OnPostUploadAsync_PreFillsInputFromValidFile()
    {
        var model = CreateModel();

        var result = await model.OnPostUploadAsync(MakeFile(ValidExportJson));

        Assert.IsType<PageResult>(result);
        Assert.True(model.ModelState.IsValid);
        Assert.True(model.HasImportedFile);
        Assert.Equal("imported-config", model.Input.Id);
        Assert.Equal(IntegrationType.MicrosoftBilling, model.Input.IntegrationType);
        Assert.Equal("billing-account-1", model.Input.BillingAccountId);
        Assert.Equal("drive-1", model.Input.DriveId);
    }

    [Fact]
    public async Task OnPostUploadAsync_RejectsMissingFile()
    {
        var model = CreateModel();

        var result = await model.OnPostUploadAsync(null);

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        Assert.False(model.HasImportedFile);
    }

    [Fact]
    public async Task OnPostUploadAsync_RejectsInvalidJson()
    {
        var model = CreateModel();

        var result = await model.OnPostUploadAsync(MakeFile("{ not valid json"));

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        Assert.False(model.HasImportedFile);
    }

    [Fact]
    public async Task OnPostUploadAsync_RejectsFileMissingRequiredFields()
    {
        var model = CreateModel();

        var result = await model.OnPostUploadAsync(MakeFile("""{ "id": "only-an-id" }"""));

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        Assert.False(model.HasImportedFile);
    }

    [Fact]
    public async Task OnPostAsync_CreatesInactiveConfiguration_WhenFolderAndBillingVerify()
    {
        var verifiedFolder = new OneDriveFolder("drive-1", "Drive One", "folder-1", "/Bills/Imported");
        var repository = new FakeConfigurationRepository();
        var service = new InvoiceConfigurationService(repository);
        var model = CreateModel(
            repository: repository,
            billingAccounts: [new BillingAccountChoice("billing-account-1", "Account", "Business")],
            verifiedFolder: verifiedFolder);
        await model.OnPostUploadAsync(MakeFile(ValidExportJson));
        model.ConfirmedFolderSelection = true;
        model.ConfirmedBillingAccountSelection = true;

        var result = await model.OnPostAsync();

        Assert.IsType<RedirectToPageResult>(result);
        var stored = await service.GetAsync(
            new InvoiceConfigurationId("imported-config"), IntegrationType.MicrosoftBilling, CancellationToken.None);
        if (stored is not StoredInvoiceConfiguration configuration)
        {
            Assert.Fail("Expected the imported configuration to have been created.");
            return;
        }
        Assert.False(configuration.Configuration.IsActive);
        Assert.Equal("Imported invoice", configuration.Configuration.InvoiceDescription);
    }

    [Fact]
    public async Task OnPostAsync_RejectsBillingAccountNotReturnedByDiscovery()
    {
        // The imported billing account ID is not re-verified against this environment's
        // discovery list, so the save must fail rather than trust the file outright.
        var model = CreateModel(billingAccounts: [], verifiedFolder: new OneDriveFolder("drive-1", "Drive One", "folder-1", "/Bills/Imported"));
        await model.OnPostUploadAsync(MakeFile(ValidExportJson));
        model.ConfirmedFolderSelection = true;
        model.ConfirmedBillingAccountSelection = true;

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
    }

    [Fact]
    public async Task OnPostAsync_RejectsFolderThatDoesNotVerifyAgainstGraph()
    {
        // The imported OneDrive folder IDs belong to another environment/tenant: Graph lookup in
        // this environment returns null, so the folder must not be accepted unverified.
        var model = CreateModel(
            billingAccounts: [new BillingAccountChoice("billing-account-1", "Account", "Business")],
            verifiedFolder: null);
        await model.OnPostUploadAsync(MakeFile(ValidExportJson));
        model.ConfirmedFolderSelection = true;
        model.ConfirmedBillingAccountSelection = true;

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
    }

    [Fact]
    public async Task OnPostAsync_RejectsUnconfirmedFolder_EvenWhenFolderAndBillingVerify()
    {
        // The reviewed folder/billing account must be actively confirmed, not just left as
        // pre-filled: an imported ID resolving successfully in this environment (e.g. dev and
        // prod sharing a tenant) doesn't mean it's the *correct* destination for this environment.
        var verifiedFolder = new OneDriveFolder("drive-1", "Drive One", "folder-1", "/Bills/Imported");
        var model = CreateModel(
            billingAccounts: [new BillingAccountChoice("billing-account-1", "Account", "Business")],
            verifiedFolder: verifiedFolder);
        await model.OnPostUploadAsync(MakeFile(ValidExportJson));
        model.ConfirmedBillingAccountSelection = true;

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
    }

    [Fact]
    public async Task OnPostAsync_RejectsUnconfirmedBillingAccount_EvenWhenFolderAndBillingVerify()
    {
        var verifiedFolder = new OneDriveFolder("drive-1", "Drive One", "folder-1", "/Bills/Imported");
        var model = CreateModel(
            billingAccounts: [new BillingAccountChoice("billing-account-1", "Account", "Business")],
            verifiedFolder: verifiedFolder);
        await model.OnPostUploadAsync(MakeFile(ValidExportJson));
        model.ConfirmedFolderSelection = true;

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
    }

    [Fact]
    public async Task OnPostAsync_DoesNotRequireBillingConfirmation_ForGraphEmailImport()
    {
        // A GraphEmail import has no billing account at all, so only the folder confirmation
        // applies - the billing checkbox must not block a save that never shows it.
        const string graphEmailExportJson = """
            {
              "id": "imported-email-config",
              "integrationType": "GraphEmail",
              "senderEmailAddress": "billing@example.com",
              "bodyPattern": "Invoice \\d+",
              "invoiceDescription": "Imported invoice",
              "frequency": "Monthly",
              "defaultVatMode": "Exclusive",
              "oneDriveFolder": {
                "driveId": "drive-1",
                "driveName": "Drive One",
                "folderItemId": "folder-1",
                "folderPath": "/Bills/Imported"
              },
              "startDate": "2025-01-01",
              "dateToleranceDays": 5
            }
            """;
        var verifiedFolder = new OneDriveFolder("drive-1", "Drive One", "folder-1", "/Bills/Imported");
        var model = CreateModel(verifiedFolder: verifiedFolder);
        await model.OnPostUploadAsync(MakeFile(graphEmailExportJson));
        model.ConfirmedFolderSelection = true;

        var result = await model.OnPostAsync();

        Assert.IsType<RedirectToPageResult>(result);
    }

    private static IFormFile MakeFile(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "export.json");
    }

    private static ImportModel CreateModel(
        FakeConfigurationRepository? repository = null,
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
        var model = new ImportModel(
            new InvoiceConfigurationService(repository ?? new FakeConfigurationRepository()),
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
