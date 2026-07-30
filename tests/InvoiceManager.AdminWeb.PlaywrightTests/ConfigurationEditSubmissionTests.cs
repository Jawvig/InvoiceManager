using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace InvoiceManager.AdminWeb.PlaywrightTests;

// Coverage for a review finding on PR #91: InvoiceConfigurationService.UpdateAsync could return a
// duplicate-search-criteria failure that EditModel.OnPostAsync didn't handle, so editing a
// configuration into another one's search criteria produced AdminWeb's generic error page instead
// of the normal inline validation message. Now that the service reports this through
// InvoiceConfigurationMutationResult rather than throwing, this is impossible to get wrong at any
// call site - the compiler forces every case to be handled - but this test still exercises the
// real behaviour end-to-end.
[Collection("AdminWebAppHost")]
[Trait("Category", "Integration")]
public sealed class ConfigurationEditSubmissionTests(AdminWebAppHostFixture appHost)
{
    [Fact]
    public async Task EditingIntoAnotherConfigurations_SearchCriteria_ShowsInlineErrorNotGenericErrorPage()
    {
        using var playwright = await Playwright.CreateAsync();
        var (browser, page) = await AdminWebSignedInPageFactory.CreateAsync(playwright);
        await using var _ = browser;

        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var otherDescription = $"Playwright Edit Other {uniqueSuffix}";
        var editedDescription = $"Playwright Edit Target {uniqueSuffix}";

        await CreateGraphEmailConfigurationAsync(page, otherDescription, $"Invoice {uniqueSuffix} \\d+");
        await CreateGraphEmailConfigurationAsync(page, editedDescription, $"Different {uniqueSuffix} \\d+");

        await page.Locator("section.status-panel", new PageLocatorOptions { HasText = editedDescription })
            .Locator("a", new LocatorLocatorOptions { HasText = "Edit" })
            .ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/Configurations/Edit"));

        // Edit this configuration's body pattern to collide with the other configuration's.
        await page.Locator("#Input_BodyPattern").FillAsync($"Invoice {uniqueSuffix} \\d+");
        await page.Locator("button[type=submit]", new PageLocatorOptions { HasText = "Save configuration" }).ClickAsync();

        // Must stay on the Edit page with the actionable validation message, not redirect to
        // Index (success) or fall through to AdminWeb's generic /Error page.
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/Configurations/Edit"));
        await Assertions.Expect(page.Locator(".notice.warning"))
            .ToContainTextAsync("already has the same search criteria");
    }

    private async Task CreateGraphEmailConfigurationAsync(IPage page, string description, string bodyPattern)
    {
        await page.GotoAsync(new Uri(appHost.AdminWebUrl, "/Configurations/Create").ToString());
        await page.Locator("#Input_IntegrationType").SelectOptionAsync("GraphEmail");
        await page.Locator("#Input_InvoiceDescription").FillAsync(description);
        await page.Locator("#Input_SenderEmailAddress").FillAsync("billing@example.com");
        await page.Locator("#Input_BodyPattern").FillAsync(bodyPattern);
        await page.EvalOnSelectorAsync("#Input_DriveId", "(el, v) => el.value = v", TestOneDriveFolder.DriveId);
        await page.EvalOnSelectorAsync("#Input_DriveName", "(el, v) => el.value = v", TestOneDriveFolder.DriveName);
        await page.EvalOnSelectorAsync("#Input_FolderItemId", "(el, v) => el.value = v", TestOneDriveFolder.FolderItemId);
        await page.EvalOnSelectorAsync("#Input_FolderPath", "el => el.value = '/Bills'");
        await page.Locator("button[type=submit]", new PageLocatorOptions { HasText = "Save configuration" }).ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/Configurations$"));
    }
}
