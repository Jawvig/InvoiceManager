using Azure.Identity;
using InvoiceManager.Core;
using InvoiceManager.Core.Repositories;
using InvoiceManager.Infrastructure.CosmosDb;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((context, services) =>
    {
        var cosmosEndpoint = context.Configuration["CosmosEndpoint"]
            ?? throw new InvalidOperationException("CosmosEndpoint configuration is required.");
        var databaseName = context.Configuration["CosmosDatabase"] ?? "invoicemanager";

        services.AddSingleton(_ =>
            new CosmosClient(cosmosEndpoint, new DefaultAzureCredential(),
                CosmosInvoiceConfigurationRepository.BuildClientOptions()));

        services.AddSingleton<IInvoiceConfigurationRepository>(sp =>
            new CosmosInvoiceConfigurationRepository(sp.GetRequiredService<CosmosClient>(), databaseName));

        services.AddSingleton<IInvoiceRecordRepository>(sp =>
            new CosmosInvoiceRecordRepository(sp.GetRequiredService<CosmosClient>(), databaseName));

        services.AddSingleton<ExpectedRecordGenerator>();
    })
    .Build();

host.Run();
