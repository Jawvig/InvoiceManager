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

    var functions = builder
        .AddAzureFunctionsProject<Projects.InvoiceManager_Functions>("functions")
        .WithReference(cosmos)
        // CosmosClientFactory reads ConnectionStrings:cosmos. The Azure Functions
        // integration surfaces the reference as cosmos__accountEndpoint instead, so
        // inject the connection string explicitly to keep the factory working.
        .WithEnvironment("ConnectionStrings__cosmos", cosmos.Resource.ConnectionStringExpression)
        .WaitFor(cosmos)
        .WaitForCompletion(seeder)
        .WithHttpHealthCheck("/api/health");

    builder
        .AddProject<Projects.InvoiceManager_AdminWeb>("adminweb")
        .WithReference(cosmos)
        .WithEnvironment(
            "MicrosoftAuthorization__TenantId",
            builder.Configuration["MicrosoftAuthorization:TenantId"] ?? "00000000-0000-0000-0000-000000000000")
        .WithEnvironment(
            "MicrosoftAuthorization__ClientId",
            builder.Configuration["MicrosoftAuthorization:ClientId"] ?? "00000000-0000-0000-0000-000000000001")
        .WithEnvironment(
            "MicrosoftAuthorization__ClientSecret",
            builder.Configuration["MicrosoftAuthorization:ClientSecret"] ?? "local-development-placeholder")
        .WithEnvironment(
            "MicrosoftAuthorization__KeyVaultUri",
            builder.Configuration["MicrosoftAuthorization:KeyVaultUri"] ?? "https://localhost/")
        .WithEnvironment("Functions__BaseUrl", functions.GetEndpoint("http"))
        .WaitFor(cosmos)
        .WaitForCompletion(seeder)
        .WaitFor(functions)
        .WithHttpHealthCheck("/health");
}

builder.Build().Run();
