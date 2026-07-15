using System.Globalization;
using Azure.Identity;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureAppConfiguration(config =>
    {
        // Load MicrosoftAuthorization--* secrets (notably ClientSecret) from Key Vault,
        // authenticating with DefaultAzureCredential (developer credentials locally, the
        // Function App's managed identity in Azure). Added after the other sources so the
        // vault wins, matching the admin website. ClientSecret is the Entra app secret MSAL
        // uses to redeem M365 tokens; it is not used to reach Key Vault itself.
        // Fail fast when no Key Vault is configured: the Functions app cannot acquire M365
        // tokens without the ClientSecret (and token cache) stored there, so continuing would
        // only defer the failure to the first invocation and make it harder to diagnose.
        var keyVaultUri = config.Build().GetValue<Uri?>("MicrosoftAuthorization:KeyVaultUri")
            ?? throw new InvalidOperationException(
                "MicrosoftAuthorization:KeyVaultUri is required. Set it in user-secrets or as an " +
                "environment variable so the Functions app can load MicrosoftAuthorization secrets.");
        config.AddAzureKeyVault(keyVaultUri, new DefaultAzureCredential());
    })
    .ConfigureServices((context, services) =>
    {
        var databaseName = context.Configuration["CosmosDatabase"] ?? "invoicemanager";

        // Export the workflow's custom spans (and the outbound HTTP calls under them) so a
        // human can view the trace tree. Locally, Aspire injects OTEL_EXPORTER_OTLP_ENDPOINT
        // and the spans appear in the Aspire dashboard; without that endpoint (e.g. in Azure)
        // no OTLP exporter is registered, so this stays quiet and the Functions runtime's own
        // Application Insights export (host.json) continues to carry invocation telemetry.
        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing.AddSource(Telemetry.ActivitySourceName);
                tracing.AddHttpClientInstrumentation();

                if (!string.IsNullOrWhiteSpace(context.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
                    tracing.AddOtlpExporter();
            });

        services.AddSingleton(_ => CosmosClientFactory.Create(context.Configuration));

        services.AddSingleton<IInvoiceConfigurationRepository>(sp =>
            new CosmosInvoiceConfigurationRepository(sp.GetRequiredService<CosmosClient>(), databaseName));

        services.AddSingleton<IInvoiceRecordRepository>(sp =>
            new CosmosInvoiceRecordRepository(
                sp.GetRequiredService<CosmosClient>(),
                databaseName,
                sp.GetRequiredService<ILogger<CosmosInvoiceRecordRepository>>()));

        services.AddSingleton<ExpectedRecordGenerator>();

        // Microsoft delegated authentication (reuses the MSAL cache the admin website
        // persisted to Key Vault) for both the billing API and OneDrive uploads.
        // Validate at startup (like the admin website) so a missing TenantId/ClientId/
        // ClientSecret fails fast here rather than at the first M365 token acquisition.
        // ClientSecret is loaded from Key Vault above, so it is present by validation time.
        services.AddOptions<MicrosoftAuthorizationOptions>()
            .Bind(context.Configuration.GetSection(MicrosoftAuthorizationOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MicrosoftAuthorizationOptions>, MicrosoftAuthorizationOptionsValidator>();
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

        // Named HttpClient so handler lifetimes rotate; auth is applied per request.
        services.AddHttpClient(nameof(MicrosoftBillingInvoiceSource));
        services.AddTransient<IInvoiceSourceIntegration>(sp =>
            CreateMicrosoftBillingInvoiceSource(sp, IntegrationType.Microsoft365));
        services.AddTransient<IInvoiceSourceIntegration>(sp =>
            CreateMicrosoftBillingInvoiceSource(sp, IntegrationType.Azure));

        static MicrosoftBillingInvoiceSource CreateMicrosoftBillingInvoiceSource(
            IServiceProvider serviceProvider,
            IntegrationType integrationType)
        {
            var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            return new MicrosoftBillingInvoiceSource(
                factory.CreateClient(nameof(MicrosoftBillingInvoiceSource)),
                serviceProvider.GetRequiredService<IMicrosoftTokenProvider>(),
                serviceProvider.GetRequiredService<IOptions<MicrosoftBillingOptions>>(),
                serviceProvider.GetRequiredService<ILogger<MicrosoftBillingInvoiceSource>>(),
                integrationType);
        }

        // Graph client gets the standard resilience handler (429/503 + Retry-After, timeouts).
        services.AddGraphOneDriveIntegration();

        services.AddSingleton(TimeProvider.System);
        services.AddTransient<DueInvoiceProcessor>();
    })
    .Build();

host.Run();
