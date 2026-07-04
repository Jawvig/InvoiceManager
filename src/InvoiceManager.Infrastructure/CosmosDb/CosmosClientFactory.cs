using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;

namespace InvoiceManager.Infrastructure.CosmosDb;

public static class CosmosClientFactory
{
    public static CosmosClient Create(IConfiguration configuration)
    {
        var options = CosmosInvoiceConfigurationRepository.BuildClientOptions();

        var connectionString = configuration.GetConnectionString("cosmos")
            ?? configuration["CosmosConnectionString"];
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            ConfigureEmulatorOptionsIfNeeded(options, connectionString);
            return new CosmosClient(connectionString, options);
        }

        var cosmosEndpoint = configuration["CosmosEndpoint"]
            ?? throw new InvalidOperationException(
                "Cosmos configuration is required. Set ConnectionStrings:cosmos, CosmosConnectionString, or CosmosEndpoint.");

        ConfigureEmulatorOptionsIfNeeded(options, cosmosEndpoint);
        return new CosmosClient(cosmosEndpoint, new DefaultAzureCredential(), options);
    }

    private static void ConfigureEmulatorOptionsIfNeeded(CosmosClientOptions options, string value)
    {
        if (!value.Contains("localhost", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        options.ConnectionMode = ConnectionMode.Gateway;
        options.LimitToEndpoint = true;
        options.ServerCertificateCustomValidationCallback = (_, _, _) => true;
    }
}
