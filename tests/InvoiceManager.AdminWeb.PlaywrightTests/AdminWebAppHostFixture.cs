using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceManager.AdminWeb.PlaywrightTests;

// Starts the real AppHost orchestration (Cosmos emulator, seeder, Functions, AdminWeb) so tests
// in this collection exercise AdminWeb with its actual dependencies available, the same way a
// developer runs it locally, rather than a standalone launch with everything else missing.
public sealed class AdminWebAppHostFixture : IAsyncLifetime
{
    private DistributedApplication? app;

    public Uri AdminWebUrl { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.InvoiceManager_AppHost>(cancellationToken: timeout.Token);
        app = await appHost.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);

        var notifications = app.Services.GetRequiredService<ResourceNotificationService>();
        await notifications.WaitForResourceHealthyAsync("adminweb", timeout.Token);

        // Resolved from the running orchestration rather than hardcoded: AppHost assigns the
        // AdminWeb port, so a fixed URL would silently rot the moment that assignment changes.
        AdminWebUrl = app.GetEndpoint("adminweb", "https");
    }

    public async Task DisposeAsync()
    {
        if (app is not null)
        {
            await app.DisposeAsync();
        }
    }
}

[CollectionDefinition("AdminWebAppHost")]
public class AdminWebAppHostCollection : ICollectionFixture<AdminWebAppHostFixture> { }
