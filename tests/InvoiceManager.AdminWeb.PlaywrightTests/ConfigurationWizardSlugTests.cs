using Microsoft.Playwright;

namespace InvoiceManager.AdminWeb.PlaywrightTests;

// Parity coverage for the Configuration ID (slug) freeze semantics implemented in
// wwwroot/js/site.js (regenerateConfigurationSlug). This was explicitly called out as
// easy to get subtly wrong: description-input and integration-change both regenerate the
// slug until the user hand-edits the ID field directly, after which neither may overwrite it.
[Collection("AdminWebAppHost")]
[Trait("Category", "Integration")]
public sealed class ConfigurationWizardSlugTests(AdminWebAppHostFixture appHost)
{
    [Fact]
    public async Task ConfigurationId_TracksDescriptionThenIntegration_UntilHandEdited()
    {
        using var playwright = await Playwright.CreateAsync();
        var (browser, page) = await AdminWebSignedInPageFactory.CreateAsync(playwright);
        await using var _ = browser;

        await page.GotoAsync(new Uri(appHost.AdminWebUrl, "/Configurations/Create").ToString());

        // Selecting an integration reveals the Configuration ID field for the first time and
        // seeds it via the same change-triggered regeneration path used later in this test.
        await page.Locator("#Input_IntegrationType").SelectOptionAsync("MicrosoftBilling");
        var idField = page.Locator("#Input_Id");
        await Assertions.Expect(idField).ToHaveValueAsync("microsoftbilling-invoice");

        // Scenario 1: description changes before any manual edit -> ID updates.
        await page.Locator("#Input_InvoiceDescription").FillAsync("Contoso Cloud Services");
        await Assertions.Expect(idField).ToHaveValueAsync("contoso-cloud-services");

        // Scenario 3: integration changes before any manual edit -> ID still tracks (description
        // is non-blank here, so the description-derived slug is unaffected by the integration
        // fallback text; assert it stays in sync with the description as integration changes).
        await page.Locator("#Input_IntegrationType").SelectOptionAsync("GraphEmail");
        await Assertions.Expect(idField).ToHaveValueAsync("contoso-cloud-services");

        // Clear the description so the fallback (integration-derived) text is exercised, then
        // confirm switching integration updates the ID to the new integration's fallback.
        await page.Locator("#Input_InvoiceDescription").FillAsync("");
        await Assertions.Expect(idField).ToHaveValueAsync("graphemail-invoice");
        await page.Locator("#Input_IntegrationType").SelectOptionAsync("MicrosoftBilling");
        await Assertions.Expect(idField).ToHaveValueAsync("microsoftbilling-invoice");

        // Manually edit the ID field directly: this must freeze it against further
        // description/integration-driven regeneration.
        await idField.FillAsync("my-custom-id");
        await idField.DispatchEventAsync("change");

        // Scenario 2: integration changes after a manual ID edit -> ID does NOT change.
        await page.Locator("#Input_IntegrationType").SelectOptionAsync("GraphEmail");
        await Assertions.Expect(idField).ToHaveValueAsync("my-custom-id");

        // Description changes after a manual ID edit -> ID does NOT change either.
        await page.Locator("#Input_InvoiceDescription").FillAsync("Something Else Entirely");
        await Assertions.Expect(idField).ToHaveValueAsync("my-custom-id");
    }
}
