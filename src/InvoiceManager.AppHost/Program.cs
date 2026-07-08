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

    // Microsoft delegated auth settings. The admin website captures the token cache and
    // both it and the Functions app read it back from the same Key Vault, so both need
    // identical values. Real values come from AppHost user-secrets; the placeholders keep
    // local infra starting when they are absent (the M365 flow itself stays unconfigured).
    var microsoftAuthTenantId =
        builder.Configuration["MicrosoftAuthorization:TenantId"] ?? "00000000-0000-0000-0000-000000000000";
    var microsoftAuthClientId =
        builder.Configuration["MicrosoftAuthorization:ClientId"] ?? "00000000-0000-0000-0000-000000000001";
    var microsoftAuthClientSecret =
        builder.Configuration["MicrosoftAuthorization:ClientSecret"] ?? "local-development-placeholder";
    var microsoftAuthKeyVaultUri =
        builder.Configuration["MicrosoftAuthorization:KeyVaultUri"] ?? "https://localhost/";

    var functions = builder
        .AddAzureFunctionsProject<Projects.InvoiceManager_Functions>("functions")
        .WithReference(cosmos)
        // CosmosClientFactory reads ConnectionStrings:cosmos. The Azure Functions
        // integration surfaces the reference as cosmos__accountEndpoint instead, so
        // inject the connection string explicitly to keep the factory working.
        .WithEnvironment("ConnectionStrings__cosmos", cosmos.Resource.ConnectionStringExpression)
        // Forward the Microsoft auth config so the Functions app reads the shared token
        // cache from the real Key Vault instead of the placeholder in local.settings.json
        // (an unreachable placeholder vault otherwise throws when the cache is accessed).
        .WithEnvironment("MicrosoftAuthorization__TenantId", microsoftAuthTenantId)
        .WithEnvironment("MicrosoftAuthorization__ClientId", microsoftAuthClientId)
        .WithEnvironment("MicrosoftAuthorization__ClientSecret", microsoftAuthClientSecret)
        .WithEnvironment("MicrosoftAuthorization__KeyVaultUri", microsoftAuthKeyVaultUri)
        .WaitFor(cosmos)
        .WaitForCompletion(seeder)
        .WithHttpHealthCheck("/api/health");

    builder
        .AddProject<Projects.InvoiceManager_AdminWeb>("adminweb")
        .WithReference(cosmos)
        .WithEnvironment("MicrosoftAuthorization__TenantId", microsoftAuthTenantId)
        .WithEnvironment("MicrosoftAuthorization__ClientId", microsoftAuthClientId)
        .WithEnvironment("MicrosoftAuthorization__ClientSecret", microsoftAuthClientSecret)
        .WithEnvironment("MicrosoftAuthorization__KeyVaultUri", microsoftAuthKeyVaultUri)
        .WithEnvironment("Functions__BaseUrl", functions.GetEndpoint("http"))
        .WaitFor(cosmos)
        .WaitForCompletion(seeder)
        .WaitFor(functions)
        .WithHttpHealthCheck("/health");
}

builder.Build().Run();
