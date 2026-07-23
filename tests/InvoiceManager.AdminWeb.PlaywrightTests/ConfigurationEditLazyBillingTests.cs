using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace InvoiceManager.AdminWeb.PlaywrightTests;

// Edit-page coverage for Task 3.5's lazy billing-account loading: everything must render visible
// immediately (integration type is fixed post-creation), and the billing account list is fetched
// in the background without blocking the page.
[Collection("AdminWebAppHost")]
[Trait("Category", "Integration")]
public sealed class ConfigurationEditLazyBillingTests(AdminWebAppHostFixture appHost)
{
    [Fact]
    public async Task EditPage_RendersEverythingVisibleImmediately_AndLoadsBillingAccountsInBackground()
    {
        using var playwright = await Playwright.CreateAsync();
        var (browser, page) = await AdminWebSignedInPageFactory.CreateAsync(playwright);
        await using var _ = browser;

        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var description = $"Playwright Billing Edit {uniqueSuffix}";

        // Create a MicrosoftBilling configuration first so there is something to edit.
        await page.GotoAsync(new Uri(appHost.AdminWebUrl, "/Configurations/Create").ToString());
        await page.Locator("#Input_IntegrationType").SelectOptionAsync("MicrosoftBilling");
        await page.Locator("#Input_InvoiceDescription").FillAsync(description);
        // Create verifies the submitted folder against Microsoft Graph, so this must be a real,
        // resolvable drive/item ID rather than a fabricated one — see TestOneDriveFolder.
        await page.EvalOnSelectorAsync("#Input_DriveId", "(el, v) => el.value = v", TestOneDriveFolder.DriveId);
        await page.EvalOnSelectorAsync("#Input_DriveName", "(el, v) => el.value = v", TestOneDriveFolder.DriveName);
        await page.EvalOnSelectorAsync("#Input_FolderItemId", "(el, v) => el.value = v", TestOneDriveFolder.FolderItemId);
        await page.EvalOnSelectorAsync("#Input_FolderPath", "el => el.value = '/Bills'");

        // Build() requires a billing account returned by discovery, so wait for the
        // configuration-wizard.js background fetch to populate at least one real option before
        // submitting (mirrors what a real user would see: the field starts empty/loading).
        // <option> elements are never independently "visible" to Playwright's actionability
        // checks, so poll the option count directly rather than waiting for visibility.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        int optionCount;
        do
        {
            optionCount = await page.Locator("#Input_BillingAccountId option").CountAsync();
            if (optionCount > 0) break;
            await page.WaitForTimeoutAsync(250);
        } while (DateTime.UtcNow < deadline);
        Assert.True(optionCount > 0, "Expected at least one billing account option to have loaded.");

        // The option's rendered label must actually come from account.displayName — this would
        // still pass with just an option-count assertion even if the client read a field that no
        // longer exists (e.g. the old "label" property) and rendered "undefined" text.
        var firstOption = page.Locator("#Input_BillingAccountId option").First;
        var firstLabel = await firstOption.TextContentAsync();
        Assert.False(string.IsNullOrWhiteSpace(firstLabel), "Expected the loaded option to have a non-empty label.");
        Assert.DoesNotContain("undefined", firstLabel);

        // Loaded options replace the (previously empty) selection, so the user must actively
        // pick one before submitting — mirrors real usage.
        var firstValue = await firstOption.GetAttributeAsync("value");
        await page.Locator("#Input_BillingAccountId").SelectOptionAsync(new[] { firstValue! });

        await page.Locator("button[type=submit]", new PageLocatorOptions { HasText = "Save configuration" }).ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/Configurations$"));

        await page.Locator("section.status-panel", new PageLocatorOptions { HasText = description })
            .Locator("a", new LocatorLocatorOptions { HasText = "Edit" })
            .ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/Configurations/Edit"));

        // On Edit, everything is visible immediately — no wizard gating.
        await Assertions.Expect(page.Locator("#configuration-id-field")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#common-details")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#billing-details")).ToBeVisibleAsync();

        // The background billing-account fetch must resolve without user interaction (it is not
        // gated behind selecting the integration, since it's already fixed).
        await Assertions.Expect(page.Locator("#billing-account-status"))
            .Not.ToHaveTextAsync("Loading billing accounts…", new LocatorAssertionsToHaveTextOptions { Timeout = 10000 });
    }
}
