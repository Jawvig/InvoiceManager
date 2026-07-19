using System.Diagnostics;
using Microsoft.Playwright;

var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
var authDir = Path.Combine(repoRoot, "playwright", ".auth");
Directory.CreateDirectory(authDir);
var storageStatePath = Path.Combine(authDir, "adminweb.json");
var edgeProfileDir = Path.Combine(authDir, "edge-profile");

const string adminWebUrl = "https://localhost:5001";

Console.WriteLine("Starting AdminWeb standalone (dotnet run --launch-profile https)...");
var adminWebProcess = StartAdminWeb(repoRoot);

try
{
    await WaitForHealthyAsync($"{adminWebUrl}/health", TimeSpan.FromMinutes(2));

    Console.WriteLine("AdminWeb is up. Launching Edge for interactive sign-in...");
    using var playwright = await Playwright.CreateAsync();
    await using var context = await playwright.Chromium.LaunchPersistentContextAsync(
        edgeProfileDir,
        new BrowserTypeLaunchPersistentContextOptions
        {
            Channel = "msedge",
            Headless = false,
        });

    var page = context.Pages.Count > 0 ? context.Pages[0] : await context.NewPageAsync();
    await page.GotoAsync(adminWebUrl);

    Console.WriteLine("Complete the Microsoft sign-in in the opened Edge window...");
    await page.WaitForURLAsync(url => !url.Contains("/signin-oidc", StringComparison.OrdinalIgnoreCase), new PageWaitForURLOptions
    {
        Timeout = 5 * 60 * 1000,
    });

    await context.StorageStateAsync(new BrowserContextStorageStateOptions { Path = storageStatePath });
    Console.WriteLine($"Saved storage state to {storageStatePath}");
}
finally
{
    StopAdminWeb(adminWebProcess);
}

return;

static Process StartAdminWeb(string repoRoot)
{
    var adminWebProjectDir = Path.Combine(repoRoot, "src", "InvoiceManager.AdminWeb");
    var startInfo = new ProcessStartInfo
    {
        FileName = "dotnet",
        WorkingDirectory = adminWebProjectDir,
        UseShellExecute = false,
    };
    startInfo.ArgumentList.Add("run");
    startInfo.ArgumentList.Add("--launch-profile");
    startInfo.ArgumentList.Add("https");

    var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Failed to start the AdminWeb process.");
    return process;
}

static void StopAdminWeb(Process process)
{
    if (process.HasExited)
    {
        return;
    }

    Console.WriteLine("Stopping AdminWeb...");
    try
    {
        // `dotnet run` spawns the actual app as a child process; kill the whole tree so the
        // inner Kestrel process doesn't linger and hold onto port 5001.
        process.Kill(entireProcessTree: true);
        process.WaitForExit(TimeSpan.FromSeconds(10));
    }
    catch (InvalidOperationException)
    {
        // Process already exited between the HasExited check and Kill.
    }
}

static async Task WaitForHealthyAsync(string healthUrl, TimeSpan timeout)
{
    using var handler = new HttpClientHandler
    {
        // The local dev Kestrel certificate is self-signed and not in the trust store.
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
    };
    using var client = new HttpClient(handler);

    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        try
        {
            var response = await client.GetAsync(healthUrl);
            if (response.IsSuccessStatusCode)
            {
                return;
            }
        }
        catch (HttpRequestException)
        {
            // AdminWeb hasn't started listening yet; keep polling.
        }

        await Task.Delay(TimeSpan.FromSeconds(1));
    }

    throw new TimeoutException($"AdminWeb did not become healthy at {healthUrl} within {timeout}.");
}

static string FindRepoRoot(string startDirectory)
{
    var directory = new DirectoryInfo(startDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "InvoiceManager.slnx")))
    {
        directory = directory.Parent;
    }

    return directory?.FullName
        ?? throw new InvalidOperationException($"Could not locate InvoiceManager.slnx above {startDirectory}.");
}
