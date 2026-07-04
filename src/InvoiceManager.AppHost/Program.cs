using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var cosmos = builder.AddAzureCosmosDB("cosmos").RunAsEmulator();

if (builder.Configuration.GetValue("AppHost:IncludeApplications", true))
{
    var functions = builder
        .AddAzureFunctionsProject<Projects.InvoiceManager_Functions>("functions")
        .WithReference(cosmos)
        // CosmosClientFactory reads ConnectionStrings:cosmos. The Azure Functions
        // integration surfaces the reference as cosmos__accountEndpoint instead, so
        // inject the connection string explicitly to keep the factory working.
        .WithEnvironment("ConnectionStrings__cosmos", cosmos.Resource.ConnectionStringExpression)
        .WaitFor(cosmos)
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
        .WaitFor(functions)
        .WithHttpHealthCheck("/health");
}

builder.Build().Run();
