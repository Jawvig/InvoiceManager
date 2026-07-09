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
        .WithArgs("--ensure-schema", seedFile)
        .WaitFor(cosmos);

    // Microsoft delegated auth settings shared by the admin website and the Functions app.
    // TenantId/ClientId/KeyVaultUri come from AppHost user-secrets (placeholders keep local
    // infra starting when absent). ClientSecret is deliberately NOT forwarded: both apps load
    // it (and any other MicrosoftAuthorization--* secret) from Key Vault via DefaultAzure
    // credentials, so it never passes through the AppHost environment.
    var microsoftAuthTenantId =
        builder.Configuration["MicrosoftAuthorization:TenantId"] ?? "00000000-0000-0000-0000-000000000000";
    var microsoftAuthClientId =
        builder.Configuration["MicrosoftAuthorization:ClientId"] ?? "00000000-0000-0000-0000-000000000001";
    var microsoftAuthKeyVaultUri =
        builder.Configuration["MicrosoftAuthorization:KeyVaultUri"] ?? "https://localhost/";

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
