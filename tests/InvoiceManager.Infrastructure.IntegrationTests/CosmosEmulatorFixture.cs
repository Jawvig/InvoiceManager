using Aspire.Hosting;
using Aspire.Hosting.Testing;
using InvoiceManager.Infrastructure.CosmosDb;
using Microsoft.Azure.Cosmos;

namespace InvoiceManager.Infrastructure.IntegrationTests;

public sealed class CosmosEmulatorFixture : IAsyncLifetime
{
    private DistributedApplication? app;

    public string ConnectionString { get; private set; } = string.Empty;
    public CosmosClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.InvoiceManager_AppHost>();
        app = await appHost.BuildAsync();
        await app.StartAsync();
        ConnectionString = await app.GetConnectionStringAsync("cosmos")
            ?? throw new InvalidOperationException(
                "Cosmos connection string was not available from the AppHost. " +
                "Ensure Docker is running and the emulator container started successfully.");

        // GetConnectionStringAsync returns as soon as the endpoint is known, before the
        // emulator process inside the container has finished initializing. Poll the
        // AccountEndpoint with a plain HttpClient until it returns any HTTP response
        // (even 401 Unauthorized means the emulator is ready). Avoid using CosmosClient
        // here: disposing a probe client corrupts shared SDK connection-pool state,
        // causing the test's own CosmosClient to hang on first use.
        var endpoint = ParseAccountEndpoint(ConnectionString);
        await WaitForEmulatorReadyAsync(endpoint, timeout: TimeSpan.FromMinutes(5));

        var options = CosmosInvoiceConfigurationRepository.BuildClientOptions();
        // The emulator runs in Docker and reports its container-internal IP in endpoint
        // discovery responses. LimitToEndpoint prevents the SDK from following those
        // redirects; it always uses the host-mapped address from the connection string.
        // ServerCertificateCustomValidationCallback bypasses the emulator's self-signed cert.
        options.LimitToEndpoint = true;
        options.ServerCertificateCustomValidationCallback = (_, _, _) => true;
        options.ConnectionMode = ConnectionMode.Gateway;
        Client = new CosmosClient(ConnectionString, options);
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (app is not null)
            await app.DisposeAsync();
    }

    private static string ParseAccountEndpoint(string connectionString)
    {
        foreach (var part in connectionString.Split(';'))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("AccountEndpoint=", StringComparison.OrdinalIgnoreCase))
                return trimmed["AccountEndpoint=".Length..].TrimEnd('/');
        }
        throw new InvalidOperationException($"AccountEndpoint not found in connection string: {connectionString}");
    }

    private static async Task WaitForEmulatorReadyAsync(string endpoint, TimeSpan timeout)
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        using var cts = new CancellationTokenSource(timeout);

        while (true)
        {
            try
            {
                await http.GetAsync(endpoint, cts.Token);
                return;
            }
            catch (Exception) when (!cts.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cts.Token).ConfigureAwait(false);
            }
        }
    }
}

[CollectionDefinition("CosmosIntegration")]
public class CosmosIntegrationCollection : ICollectionFixture<CosmosEmulatorFixture> { }
