using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var cosmos = builder.AddAzureCosmosDB("cosmos").RunAsEmulator();

if (builder.Configuration.GetValue("AppHost:IncludeApplications", true))
{
    var functionsPort = 7071;
    var functionsCommand = ResolveCommand("func") ?? "func";
    var functions = builder
        .AddExecutable(
            "functions",
            functionsCommand,
            "../InvoiceManager.Functions",
            "start",
            "--port",
            functionsPort.ToString())
        .WithHttpEndpoint(port: functionsPort, targetPort: functionsPort, isProxied: false)
        .WithReference(cosmos)
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

static string? ResolveCommand(string command)
{
    var path = Environment.GetEnvironmentVariable("PATH");
    if (string.IsNullOrWhiteSpace(path))
    {
        return null;
    }

    var extensions = OperatingSystem.IsWindows()
        ? [".cmd", ".exe", ".bat", string.Empty]
        : new[] { string.Empty };

    var directories = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
    if (OperatingSystem.IsWindows())
    {
        var npmDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "npm");
        directories = [.. directories, npmDirectory];
    }

    foreach (var directory in directories)
    {
        foreach (var extension in extensions)
        {
            var candidate = Path.Combine(directory, command + extension);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    return null;
}
