using Microsoft.Playwright;

namespace InvoiceManager.AdminWeb.PlaywrightTests;

// Shared setup for tests that need an authenticated AdminWeb page: reuses the storage state
// captured by tools/InvoiceManager.PlaywrightAuth (see docs/deployment.md) rather than driving a
// real Microsoft sign-in per test.
internal static class AdminWebSignedInPageFactory
{
    public static readonly string StorageStatePath = Path.Combine(
        FindRepoRoot(AppContext.BaseDirectory), "playwright", ".auth", "adminweb.json");

    public static async Task<(IBrowser Browser, IPage Page)> CreateAsync(IPlaywright playwright)
    {
        Assert.True(
            File.Exists(StorageStatePath),
            $"No saved Playwright storage state at '{StorageStatePath}'. Run " +
            "`dotnet run --project tools/InvoiceManager.PlaywrightAuth` to capture one, then " +
            "re-run this test.");

        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Channel = "msedge",
        });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            StorageStatePath = StorageStatePath,
            IgnoreHTTPSErrors = true,
        });
        var page = await context.NewPageAsync();
        return (browser, page);
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
