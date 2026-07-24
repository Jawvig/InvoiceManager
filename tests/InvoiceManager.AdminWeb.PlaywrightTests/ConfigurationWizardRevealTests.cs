using Microsoft.Playwright;

namespace InvoiceManager.AdminWeb.PlaywrightTests;

// Smoke coverage for the progressive-disclosure wizard and OneDrive folder picker wiring
// (element IDs, fetch handlers, dialog open/close) against the real running AdminWeb app.
[Collection("AdminWebAppHost")]
[Trait("Category", "Integration")]
public sealed class ConfigurationWizardRevealTests(AdminWebAppHostFixture appHost)
{
    [Fact]
    public async Task CreatePage_HidesDetailsUntilIntegrationChosen_ThenRevealsMatchingFieldset()
    {
        using var playwright = await Playwright.CreateAsync();
        var (browser, page) = await AdminWebSignedInPageFactory.CreateAsync(playwright);
        await using var _ = browser;

        await page.GotoAsync(new Uri(appHost.AdminWebUrl, "/Configurations/Create").ToString());

        await Assertions.Expect(page.Locator("#configuration-id-field")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#common-details")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#billing-details")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#email-details")).ToBeHiddenAsync();

        await page.Locator("#Input_IntegrationType").SelectOptionAsync("MicrosoftBilling");

        await Assertions.Expect(page.Locator("#configuration-id-field")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#common-details")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#billing-details")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#email-details")).ToBeHiddenAsync();

        await page.Locator("#Input_IntegrationType").SelectOptionAsync("GraphEmail");

        await Assertions.Expect(page.Locator("#billing-details")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#email-details")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task CreatePage_OneDrivePicker_OpensAndListsOrReportsErrorWithoutCrashing()
    {
        using var playwright = await Playwright.CreateAsync();
        var (browser, page) = await AdminWebSignedInPageFactory.CreateAsync(playwright);
        await using var _ = browser;

        await page.GotoAsync(new Uri(appHost.AdminWebUrl, "/Configurations/Create").ToString());
        await page.Locator("#Input_IntegrationType").SelectOptionAsync("MicrosoftBilling");

        await page.Locator("#onedrive-picker-open").ClickAsync();
        await Assertions.Expect(page.Locator("#onedrive-picker-dialog")).ToBeVisibleAsync();

        // Either the drive list populates or an error/empty message is shown — either way the
        // "Loading…" status must clear and the dialog must not be left in a stuck/crashed state.
        await Assertions.Expect(page.Locator("#onedrive-picker-status"))
            .Not.ToHaveTextAsync("Loading drives…", new LocatorAssertionsToHaveTextOptions { Timeout = 10000 });

        await page.Locator("#onedrive-picker-cancel").ClickAsync();
        await Assertions.Expect(page.Locator("#onedrive-picker-dialog")).ToBeHiddenAsync();
    }
}
