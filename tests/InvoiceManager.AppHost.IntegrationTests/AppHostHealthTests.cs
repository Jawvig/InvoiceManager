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
}
