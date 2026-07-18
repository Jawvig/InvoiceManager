using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

#pragma warning disable ASPIRECOSMOSDB001
var cosmos = builder.AddAzureCosmosDB("cosmos")
    .RunAsPreviewEmulator(
        emulator => emulator.WithDataExplorer()
    );
#pragma warning restore ASPIRECOSMOSDB001

if (builder.Configuration.GetValue("AppHost:IncludeApplications", true))
{
    // Bootstrap the emulator: create the database and containers (--ensure-schema) and
    // seed the invoice configurations. The cloud provisions schema via Terraform, but
    // nothing else exists to provision the emulator, so Aspire owns local schema + seed.
    // The apps WaitForCompletion on this so they never start against a missing container.
    var seedFile = Path.GetFullPath(
        Path.Combine(builder.AppHostDirectory, "..", "..", "data", "seed", "invoice-configurations.json"));

    var seeder = builder
        .AddProject<Projects.InvoiceManager_Seeder>("seeder")
        .WithReference(cosmos)
        .WithEnvironment("ConnectionStrings__cosmos", cosmos.Resource.ConnectionStringExpression)
        .WithEnvironment("CosmosDatabase", "invoicemanager")
        // Seed the local emulator as the "test" environment so every OneDrive destination is
        // nested under a root "Test" folder and local downloads never collide with production.
        .WithArgs("--ensure-schema", "--environment", "test", seedFile)
        .WaitFor(cosmos);

    // Microsoft delegated auth settings shared by the admin website and the Functions app.
    // TenantId/ClientId/KeyVaultUri come from AppHost user-secrets and are required: fail fast
    // here rather than forward placeholders that only defer the failure to the running apps.
    // ClientSecret is deliberately NOT forwarded: both apps load it (and any other
    // MicrosoftAuthorization secret) from Key Vault via DefaultAzureCredential, so it never
    // passes through the AppHost environment.
    var microsoftAuthTenantId = builder.Configuration.GetRequiredValue("MicrosoftAuthorization:TenantId");
    var microsoftAuthClientId = builder.Configuration.GetRequiredValue("MicrosoftAuthorization:ClientId");
    var microsoftAuthKeyVaultUri = builder.Configuration.GetRequiredValue("MicrosoftAuthorization:KeyVaultUri");
    // The Document Intelligence resource is provisioned by Terraform, not Aspire (there is no
    // local emulator for it), so its endpoint must point at a real deployed resource, the same
    // way the Microsoft auth values above point at the real test Key Vault.
    var documentIntelligenceEndpoint = builder.Configuration.GetRequiredValue("DocumentIntelligence:Endpoint");

    var functions = builder
        .AddAzureFunctionsProject<Projects.InvoiceManager_Functions>("functions")
        .WithReference(cosmos)
        // CosmosClientFactory reads ConnectionStrings:cosmos. The Azure Functions
        // integration surfaces the reference as cosmos__accountEndpoint instead, so
        // inject the connection string explicitly to keep the factory working.
        .WithEnvironment("ConnectionStrings__cosmos", cosmos.Resource.ConnectionStringExpression)
        // Forward the Microsoft auth config so the Functions app reaches the real Key Vault
        // (for the shared token cache and the ClientSecret) instead of a placeholder.
        .WithEnvironment("MicrosoftAuthorization__TenantId", microsoftAuthTenantId)
        .WithEnvironment("MicrosoftAuthorization__ClientId", microsoftAuthClientId)
        .WithEnvironment("MicrosoftAuthorization__KeyVaultUri", microsoftAuthKeyVaultUri)
        .WithEnvironment("DocumentIntelligence__Endpoint", documentIntelligenceEndpoint)
        .WaitFor(cosmos)
        .WaitForCompletion(seeder)
        .WithHttpHealthCheck("/api/health");

    builder
        .AddProject<Projects.InvoiceManager_AdminWeb>("adminweb")
        .WithReference(cosmos)
        .WithEnvironment("MicrosoftAuthorization__TenantId", microsoftAuthTenantId)
        .WithEnvironment("MicrosoftAuthorization__ClientId", microsoftAuthClientId)
        .WithEnvironment("MicrosoftAuthorization__KeyVaultUri", microsoftAuthKeyVaultUri)
        .WithEnvironment("Functions__BaseUrl", functions.GetEndpoint("http"))
        .WaitFor(cosmos)
        .WaitForCompletion(seeder)
        .WaitFor(functions)
        .WithHttpHealthCheck("/health");
}

builder.Build().Run();

internal static class ConfigurationExtensions
{
    /// <summary>
    /// Returns a required configuration value, failing fast when it is absent or blank so a
    /// misconfiguration surfaces at startup instead of as a deferred downstream error.
    /// </summary>
    public static string GetRequiredValue(this IConfiguration configuration, string key)
    {
        // GetRequiredSection throws when the key is absent; also reject a present-but-blank value.
        var value = configuration.GetRequiredSection(key).Value;
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"Required configuration value '{key}' is not set. Configure it in user-secrets " +
                $"for the AppHost (dotnet user-secrets set) or as an environment variable.")
            : value;
    }
}
