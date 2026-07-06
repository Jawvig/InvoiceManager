using System.Globalization;
using InvoiceManager.Core;
using InvoiceManager.Core.Integrations;
using InvoiceManager.Core.Repositories;
using InvoiceManager.Infrastructure.CosmosDb;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using InvoiceManager.Infrastructure.OneDrive;
using InvoiceManager.Integrations.Microsoft365;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((context, services) =>
    {
        var databaseName = context.Configuration["CosmosDatabase"] ?? "invoicemanager";

        services.AddSingleton(_ => CosmosClientFactory.Create(context.Configuration));

        services.AddSingleton<IInvoiceConfigurationRepository>(sp =>
            new CosmosInvoiceConfigurationRepository(sp.GetRequiredService<CosmosClient>(), databaseName));

        services.AddSingleton<IInvoiceRecordRepository>(sp =>
            new CosmosInvoiceRecordRepository(sp.GetRequiredService<CosmosClient>(), databaseName));

        services.AddSingleton<ExpectedRecordGenerator>();

        // Microsoft delegated authentication (reuses the MSAL cache the admin website
        // persisted to Key Vault) for both the billing API and OneDrive uploads.
        services.AddOptions<MicrosoftAuthorizationOptions>()
            .Bind(context.Configuration.GetSection(MicrosoftAuthorizationOptions.SectionName));
        services.AddOptions<MicrosoftBillingOptions>()
            .Bind(context.Configuration.GetSection(MicrosoftBillingOptions.SectionName));

        services.AddSingleton<IMicrosoftAuthorizationStore>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MicrosoftAuthorizationOptions>>();
            var secretStoreClient = new AzureKeyVaultSecretStoreClient(options.Value.KeyVaultUri);
            return new KeyVaultMicrosoftAuthorizationStore(secretStoreClient, options);
        });
        services.AddSingleton<IMicrosoftTokenProvider, MicrosoftTokenProvider>();

        services.AddSingleton(new InvoiceFilenameSettings { Culture = CultureInfo.GetCultureInfo("en-GB") });
        services.AddSingleton<InvoiceFilename>();

        // Typed HttpClients so handler lifetimes rotate; auth is applied per request.
        services.AddHttpClient<MicrosoftBillingInvoiceSource>();
        services.AddHttpClient<GraphOneDriveIntegration>();
        services.AddTransient<IInvoiceSourceIntegration>(sp => sp.GetRequiredService<MicrosoftBillingInvoiceSource>());
        services.AddTransient<IOneDriveIntegration>(sp => sp.GetRequiredService<GraphOneDriveIntegration>());

        services.AddSingleton(TimeProvider.System);
        services.AddTransient<DueInvoiceProcessor>();
    })
    .Build();

host.Run();
