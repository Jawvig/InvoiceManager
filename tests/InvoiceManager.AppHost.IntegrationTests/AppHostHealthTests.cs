using System.ComponentModel;
using System.Diagnostics;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceManager.AppHost.IntegrationTests;

public sealed class AppHostHealthTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task AppHost_StartsAllResources_AndReportsHealthy()
    {
        await AssertAzureFunctionsCoreToolsAvailableAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        await using var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.InvoiceManager_AppHost>(
                cancellationToken: timeout.Token);
        await using var app = await appHost.BuildAsync(timeout.Token);

        await app.StartAsync(timeout.Token);

        var notifications = app.Services.GetRequiredService<ResourceNotificationService>();
        await notifications.WaitForResourceHealthyAsync("cosmos", timeout.Token);
        await notifications.WaitForResourceHealthyAsync("functions", timeout.Token);
        await notifications.WaitForResourceHealthyAsync("adminweb", timeout.Token);
    }

    private static async Task AssertAzureFunctionsCoreToolsAvailableAsync()
    {
        var funcPath = ResolveCommand("func")
            ?? throw new InvalidOperationException(
                "Azure Functions Core Tools 'func' is required to run the AppHost integration test. " +
                "Install azure-functions-core-tools@4 and ensure func is on PATH.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = funcPath,
                ArgumentList = { "--version" },
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            });
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(
                "Azure Functions Core Tools 'func' is required to run the AppHost integration test. " +
                "Install azure-functions-core-tools@4 and ensure func is on PATH.",
                ex);
        }

        using var runningProcess = process
            ?? throw new InvalidOperationException("Azure Functions Core Tools 'func' could not be started.");

        await runningProcess.WaitForExitAsync(timeout.Token);
        if (runningProcess.ExitCode == 0)
        {
            return;
        }

        var output = await runningProcess.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = await runningProcess.StandardError.ReadToEndAsync(timeout.Token);
        throw new InvalidOperationException(
            "Azure Functions Core Tools 'func' is required to run the AppHost integration test. " +
            $"Exit code: {runningProcess.ExitCode}. Output: {output} Error: {error}");
    }

    private static string? ResolveCommand(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var extensions = OperatingSystem.IsWindows()
            ? [".cmd", ".exe", ".bat", string.Empty]
            : new[] { string.Empty };

        var directories = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        if (OperatingSystem.IsWindows())
        {
            var npmDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "npm");
            directories = [.. directories, npmDirectory];
        }

        foreach (var directory in directories)
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, command + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
