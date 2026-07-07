using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using InvoiceManager.Infrastructure.CosmosDb;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceManager.AppHost.IntegrationTests;

public sealed class SeederIntegrationTests
{
    // The IDs in data/seed/invoice-configurations.json. The seeder must load these into
    // the emulator's invoice-configurations container before the apps start.
    private static readonly string[] ExpectedConfigurationIds = ["m365-business-basic", "m365-copilot"];

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Seeder_PopulatesInvoiceConfigurations_InTheEmulator()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        await using var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.InvoiceManager_AppHost>(
                cancellationToken: timeout.Token);
        await using var app = await appHost.BuildAsync(timeout.Token);

        await app.StartAsync(timeout.Token);

        // The seeder is a run-to-completion resource; wait for it to finish rather than
        // become "healthy". Once it reaches Finished the schema exists and the
        // configurations have been written.
        var notifications = app.Services.GetRequiredService<ResourceNotificationService>();
        await notifications.WaitForResourceAsync(
            "seeder",
            e => e.Snapshot.State?.Text == KnownResourceStates.Finished,
            timeout.Token);

        var connectionString = await app.GetConnectionStringAsync("cosmos", timeout.Token)
            ?? throw new InvalidOperationException("Cosmos connection string was not available from the AppHost.");

        // Connect the same way the running app does, so the emulator's self-signed cert
        // and endpoint redirects are handled identically.
        var options = CosmosInvoiceConfigurationRepository.BuildClientOptions();
        options.ConnectionMode = ConnectionMode.Gateway;
        options.LimitToEndpoint = true;
        options.ServerCertificateCustomValidationCallback = (_, _, _) => true;
        using var client = new CosmosClient(connectionString, options);

        var repository = new CosmosInvoiceConfigurationRepository(client, "invoicemanager");
        var configurations = await repository.ListActiveAsync(timeout.Token);

        var seededIds = configurations.Select(c => c.Id.Value).ToArray();
        Assert.All(ExpectedConfigurationIds, id => Assert.Contains(id, seededIds));
    }
}
