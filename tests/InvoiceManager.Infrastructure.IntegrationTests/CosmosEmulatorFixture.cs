using System.Net;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using InvoiceManager.Infrastructure.CosmosDb;
using Microsoft.Azure.Cosmos;

namespace InvoiceManager.Infrastructure.IntegrationTests;

public sealed class CosmosEmulatorFixture : IAsyncLifetime
{
    // The account-does-not-exist substatus returned while the data plane is still
    // warming up behind an already-responsive gateway.
    private const int AccountNotReadySubStatus = 1008;

    // The "collection is not yet available for read" substatus. CreateContainer can
    // return before the emulator has made the new collection readable/writable, so the
    // first data operation against it fails with NotFound/1013 until replication catches
    // up. This is the exact cascade symptom in issue #28.
    private const int CollectionNotReadySubStatus = 1013;

    // Dedicated warm-up database, kept separate from the per-test databases so the
    // warm-up never races a test's own create/delete lifecycle.
    private const string WarmUpDatabase = "invoicemanager-warmup";
    private const string WarmUpContainer = "warmup";

    private DistributedApplication? app;

    public string ConnectionString { get; private set; } = string.Empty;
    public CosmosClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.InvoiceManager_AppHost>(
                ["--AppHost:IncludeApplications=false"]);
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
        // On a cold, loaded CI runner a single DDL call can exceed the SDK's default
        // 65s request timeout while the data plane spins up. Raise it so the warm-up
        // (and any first test operation) has headroom rather than eating the wall.
        options.RequestTimeout = TimeSpan.FromSeconds(120);
        Client = new CosmosClient(ConnectionString, options);

        // The HTTP probe only proves the gateway answers; the data plane can still be
        // unable to service DDL, which is exactly what makes the first CreateContainer
        // call in a test collection time out on CI. Warm the data plane using the real
        // (long-lived, never-disposed) client so the account/DDL are provably ready
        // before any test runs. This respects the "no throwaway/disposed CosmosClient"
        // constraint above because it reuses Client rather than creating a probe client.
        await WaitForDataPlaneReadyAsync(Client, timeout: TimeSpan.FromMinutes(3));
    }

    /// <summary>
    /// Creates the database and container if they do not exist, retrying on the transient
    /// warm-up failures the emulator surfaces on DDL. Even after the data plane is warm,
    /// the SDK caps a single metadata (create-container) call at a 65s gateway timeout
    /// regardless of <see cref="CosmosClientOptions.RequestTimeout"/>, so an individual
    /// create can still intermittently hit that wall (the exact symptom of issue #28).
    /// Tests must route their per-test DDL through this method rather than calling the
    /// client directly so that a single flaky create is retried instead of failing the run.
    /// </summary>
    public Task EnsureDatabaseAndContainerAsync(string database, ContainerProperties container) =>
        RunDdlWithRetryAsync(
            async token =>
            {
                var databaseResponse = await Client.CreateDatabaseIfNotExistsAsync(
                    database, cancellationToken: token);
                var containerResponse = await databaseResponse.Database.CreateContainerIfNotExistsAsync(
                    container, cancellationToken: token);

                // CreateContainer can return before the collection is actually available
                // for read, so probe it with a real query. A NotFound/1013 here is thrown
                // and retried by RunDdlWithRetryAsync until the collection is queryable;
                // once this succeeds the test's own operations will not race the emulator.
                await ProbeContainerReadableAsync(containerResponse.Container, token);
            },
            timeout: TimeSpan.FromMinutes(3));

    /// <summary>
    /// Deletes the database if it exists, retrying on the same transient DDL failures.
    /// A missing database is treated as success so cleanup is idempotent.
    /// </summary>
    public Task DeleteDatabaseAsync(string database) =>
        RunDdlWithRetryAsync(
            async token =>
            {
                try
                {
                    await Client.GetDatabase(database).DeleteAsync(cancellationToken: token);
                }
                catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                }
            },
            timeout: TimeSpan.FromMinutes(3));

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

    /// <summary>
    /// Drives a real create-database / create-container round trip against the emulator
    /// until it succeeds, retrying on the transient failures the emulator surfaces while
    /// its data plane is still initialising behind an already-responsive gateway
    /// (RequestTimeout, ServiceUnavailable, and Forbidden/1008 "account does not exist").
    /// Once this returns, DDL is proven to work and no test eats the cold-start timeout.
    /// </summary>
    private static Task WaitForDataPlaneReadyAsync(CosmosClient client, TimeSpan timeout) =>
        RunDdlWithRetryAsync(
            async token =>
            {
                var database = await client.CreateDatabaseIfNotExistsAsync(
                    WarmUpDatabase, cancellationToken: token);
                await database.Database.CreateContainerIfNotExistsAsync(
                    new ContainerProperties(WarmUpContainer, "/id"), cancellationToken: token);

                // Leave a clean account for the tests; the warm-up artefacts have served
                // their purpose. Best-effort: a failure here does not affect readiness.
                try
                {
                    await database.Database.DeleteAsync(cancellationToken: token);
                }
                catch (CosmosException)
                {
                }
            },
            timeout);

    /// <summary>
    /// Runs a DDL operation, retrying on the transient failures the emulator surfaces while
    /// its data plane is still initialising behind an already-responsive gateway
    /// (RequestTimeout, ServiceUnavailable, and Forbidden/1008 "account does not exist"),
    /// as well as the intermittent 65s metadata timeout the SDK enforces on create-container
    /// even once the account is warm. Retries until the operation succeeds or the deadline
    /// elapses, at which point the last transient failure propagates.
    /// </summary>
    private static async Task RunDdlWithRetryAsync(
        Func<CancellationToken, Task> operation, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);

        while (true)
        {
            try
            {
                await operation(cts.Token);
                return;
            }
            catch (Exception ex) when (IsTransientWarmUpFailure(ex) && !cts.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cts.Token).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Issues a real query against the container so a NotFound/1013 ("collection is not yet
    /// available for read") surfaces here and is retried, rather than in the test body.
    /// </summary>
    private static async Task ProbeContainerReadableAsync(Container container, CancellationToken token)
    {
        using var iterator = container.GetItemQueryIterator<int>("SELECT VALUE COUNT(1) FROM c");
        while (iterator.HasMoreResults)
            await iterator.ReadNextAsync(token);
    }

    private static bool IsTransientWarmUpFailure(Exception ex) => ex switch
    {
        CosmosException cosmos =>
            cosmos.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.ServiceUnavailable
            || (cosmos.StatusCode is HttpStatusCode.Forbidden
                && cosmos.SubStatusCode == AccountNotReadySubStatus)
            || (cosmos.StatusCode is HttpStatusCode.NotFound
                && cosmos.SubStatusCode == CollectionNotReadySubStatus),
        TaskCanceledException => true,
        HttpRequestException => true,
        _ => false,
    };
}

[CollectionDefinition("CosmosIntegration")]
public class CosmosIntegrationCollection : ICollectionFixture<CosmosEmulatorFixture> { }
