using Microsoft.Playwright;

namespace InvoiceManager.AdminWeb.PlaywrightTests;

[Trait("Category", "Integration")]
public sealed class AdminWebSignInSmokeTests
{
    private const string AdminWebUrl = "https://localhost:5001";

    // Reused, not regenerated here: tools/InvoiceManager.PlaywrightAuth captures this by driving
    // a real Microsoft sign-in once (see docs/deployment.md).
    private static readonly string StorageStatePath = Path.Combine(
        FindRepoRoot(AppContext.BaseDirectory), "playwright", ".auth", "adminweb.json");

    [Fact]
    public async Task HomePage_WithSavedStorageState_RendersPastSignIn()
    {
        Assert.True(
            File.Exists(StorageStatePath),
            $"No saved Playwright storage state at '{StorageStatePath}'. Run " +
            "`dotnet run --project tools/InvoiceManager.PlaywrightAuth` (with AdminWeb reachable " +
            "at https://localhost:5001) to capture one, then re-run this test.");

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Channel = "msedge",
        });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            StorageStatePath = StorageStatePath,
            IgnoreHTTPSErrors = true,
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync(AdminWebUrl);

        Assert.DoesNotContain("/signin-oidc", page.Url, StringComparison.OrdinalIgnoreCase);
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Microsoft authorization");
    }

    private static string FindRepoRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "InvoiceManager.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException($"Could not locate InvoiceManager.slnx above {startDirectory}.");
    }
}
