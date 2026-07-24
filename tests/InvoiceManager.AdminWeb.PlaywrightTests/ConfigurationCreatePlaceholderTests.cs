using Microsoft.Playwright;

namespace InvoiceManager.AdminWeb.PlaywrightTests;

// Coverage that the Create page's Integration select genuinely starts unselected rather than
// silently defaulting to MicrosoftBilling (which would be indistinguishable from a real choice
// and would defeat the wizard's "nothing shows until you choose" gating).
[Collection("AdminWebAppHost")]
[Trait("Category", "Integration")]
public sealed class ConfigurationCreatePlaceholderTests(AdminWebAppHostFixture appHost)
{
    [Fact]
    public async Task CreatePage_IntegrationSelect_StartsOnPlaceholder_NotMicrosoftBilling()
    {
        using var playwright = await Playwright.CreateAsync();
        var (browser, page) = await AdminWebSignedInPageFactory.CreateAsync(playwright);
        await using var _ = browser;

        await page.GotoAsync(new Uri(appHost.AdminWebUrl, "/Configurations/Create").ToString());

        var integrationSelect = page.Locator("#Input_IntegrationType");
        await Assertions.Expect(integrationSelect).ToHaveValueAsync("");

        // Nothing should be revealed yet either, confirming the placeholder isn't just
        // displayed but genuinely represents "no selection made".
        await Assertions.Expect(page.Locator("#configuration-id-field")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#common-details")).ToBeHiddenAsync();
    }
}
