using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace InvoiceManager.AdminWeb.PlaywrightTests;

// Coverage that Activate genuinely flips a configuration back to active (not just Deactivate),
// and that the audit history records the correct action for each step rather than always
// recording "Deactivated".
[Collection("AdminWebAppHost")]
[Trait("Category", "Integration")]
public sealed class ConfigurationActivateDeactivateTests(AdminWebAppHostFixture appHost)
{
    [Fact]
    public async Task ActivateThenDeactivateThenActivate_TogglesStateAndRecordsCorrectHistory()
    {
        using var playwright = await Playwright.CreateAsync();
        var (browser, page) = await AdminWebSignedInPageFactory.CreateAsync(playwright);
        await using var _ = browser;

        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var description = $"Playwright Activate Cycle {uniqueSuffix}";

        // Create a configuration (always saved inactive) so there's something to toggle.
        await page.GotoAsync(new Uri(appHost.AdminWebUrl, "/Configurations/Create").ToString());
        await page.Locator("#Input_IntegrationType").SelectOptionAsync("GraphEmail");
        await page.Locator("#Input_InvoiceDescription").FillAsync(description);
        await page.Locator("#Input_SenderEmailAddress").FillAsync("billing@example.com");
        await page.Locator("#Input_BodyPattern").FillAsync("Invoice \\d+");
        await page.EvalOnSelectorAsync("#Input_DriveId", "el => el.value = 'drive-test'");
        await page.EvalOnSelectorAsync("#Input_DriveName", "el => el.value = 'Test Drive'");
        await page.EvalOnSelectorAsync("#Input_FolderItemId", "el => el.value = 'folder-test'");
        await page.EvalOnSelectorAsync("#Input_FolderPath", "el => el.value = '/Bills'");
        await page.Locator("button[type=submit]", new PageLocatorOptions { HasText = "Save configuration" }).ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/Configurations$"));

        var panel = page.Locator("section.status-panel", new PageLocatorOptions { HasText = description });
        await Assertions.Expect(panel.GetByText("Inactive")).ToBeVisibleAsync();

        // Activate: a freshly created configuration only ever offers "Activate" first.
        await panel.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Activate" }).ClickAsync();
        panel = page.Locator("section.status-panel", new PageLocatorOptions { HasText = description });
        await Assertions.Expect(panel.GetByText("Active", new LocatorGetByTextOptions { Exact = true })).ToBeVisibleAsync();

        // Deactivate.
        await panel.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Deactivate" }).ClickAsync();
        panel = page.Locator("section.status-panel", new PageLocatorOptions { HasText = description });
        await Assertions.Expect(panel.GetByText("Inactive")).ToBeVisibleAsync();

        // Activate again: this is the step that was reported broken (button appeared to do
        // nothing, and history kept recording "Deactivated" instead of "Activated").
        await panel.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Activate" }).ClickAsync();
        panel = page.Locator("section.status-panel", new PageLocatorOptions { HasText = description });
        await Assertions.Expect(panel.GetByText("Active", new LocatorGetByTextOptions { Exact = true })).ToBeVisibleAsync();

        await panel.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "History" }).ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/Configurations/History"));

        // Newest first: second Activate, Deactivate, first Activate, Created.
        var headings = await page.Locator("section.status-panel h2").AllTextContentsAsync();
        Assert.Equal(["Activated", "Deactivated", "Activated", "Created"], headings);
    }
}
