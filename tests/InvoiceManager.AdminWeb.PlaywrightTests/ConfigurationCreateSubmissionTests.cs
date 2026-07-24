using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace InvoiceManager.AdminWeb.PlaywrightTests;

// End-to-end coverage that the reworked wizard form (progressive disclosure + hidden OneDrive
// fields populated by the picker) still round-trips through a real save. The OneDrive folder
// selection itself is set directly (mirroring what onedrive-picker.js's commitSelection() does)
// rather than drilling through the live picker UI — but it still names a real folder (see
// TestOneDriveFolder), since Create verifies the selection against Microsoft Graph.
[Collection("AdminWebAppHost")]
[Trait("Category", "Integration")]
public sealed class ConfigurationCreateSubmissionTests(AdminWebAppHostFixture appHost)
{
    [Fact]
    public async Task CreateGraphEmailConfiguration_WithWizardAndManuallyCommittedFolder_SavesAndListsOnIndex()
    {
        using var playwright = await Playwright.CreateAsync();
        var (browser, page) = await AdminWebSignedInPageFactory.CreateAsync(playwright);
        await using var _ = browser;

        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var description = $"Playwright Test Invoice {uniqueSuffix}";

        await page.GotoAsync(new Uri(appHost.AdminWebUrl, "/Configurations/Create").ToString());

        await page.Locator("#Input_IntegrationType").SelectOptionAsync("GraphEmail");
        await Assertions.Expect(page.Locator("#email-details")).ToBeVisibleAsync();

        await page.Locator("#Input_InvoiceDescription").FillAsync(description);
        await page.Locator("#Input_SenderEmailAddress").FillAsync("billing@example.com");
        await page.Locator("#Input_BodyPattern").FillAsync("Invoice \\d+");

        // Simulate the picker's commit step directly instead of drilling through the live picker
        // UI — but Create still verifies the submitted DriveId/FolderItemId against Microsoft
        // Graph, so these must be real, resolvable values (see TestOneDriveFolder), not fabricated
        // "drive-test"/"folder-test" placeholders.
        await page.EvalOnSelectorAsync("#Input_DriveId", "(el, v) => el.value = v", TestOneDriveFolder.DriveId);
        await page.EvalOnSelectorAsync("#Input_DriveName", "(el, v) => el.value = v", TestOneDriveFolder.DriveName);
        await page.EvalOnSelectorAsync("#Input_FolderItemId", "(el, v) => el.value = v", TestOneDriveFolder.FolderItemId);
        await page.EvalOnSelectorAsync("#Input_FolderPath", "el => el.value = '/Bills'");

        await page.Locator("button[type=submit]", new PageLocatorOptions { HasText = "Save configuration" }).ClickAsync();

        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/Configurations$"));
        await Assertions.Expect(page.Locator("body")).ToContainTextAsync(description);
    }
}
