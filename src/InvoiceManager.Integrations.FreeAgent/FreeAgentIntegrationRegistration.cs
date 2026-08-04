using InvoiceManager.Core.Integrations.FreeAgent;
using InvoiceManager.Infrastructure;
using InvoiceManager.Infrastructure.FreeAgentAuthorization;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace InvoiceManager.Integrations.FreeAgent;

/// <summary>
/// Registers the FreeAgent integration's public Core interfaces. Keeps the
/// wire-level client and its implementations internal to this project - callers
/// (the Functions composition root) depend only on the Core interfaces, mirroring
/// <c>GraphOneDriveRegistration</c>'s shape.
/// </summary>
public static class FreeAgentIntegrationRegistration
{
    public static IServiceCollection AddFreeAgentIntegration(this IServiceCollection services)
    {
        services.AddHttpClient<FreeAgentApiClient>().AddStandardResilienceHandler();

        services.AddSingleton<IFreeAgentAuthorizationStore>(sp =>
        {
            var keyVaultUri = sp.GetRequiredService<IOptions<KeyVaultOptions>>().Value.Uri;
            var secretStoreClient = new AzureKeyVaultSecretStoreClient(keyVaultUri);
            return new KeyVaultFreeAgentAuthorizationStore(
                secretStoreClient, sp.GetRequiredService<IOptions<FreeAgentAuthorizationOptions>>());
        });
        services.AddHttpClient(nameof(FreeAgentTokenProvider));
        services.AddSingleton<IFreeAgentTokenProvider>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new FreeAgentTokenProvider(
                factory.CreateClient(nameof(FreeAgentTokenProvider)),
                sp.GetRequiredService<IFreeAgentAuthorizationStore>(),
                sp.GetRequiredService<IOptions<FreeAgentOptions>>(),
                sp.GetRequiredService<IOptions<FreeAgentAuthorizationOptions>>());
        });

        services.AddTransient<IFreeAgentBillMatcher, FreeAgentBillMatcher>();
        services.AddTransient<IFreeAgentBillReconciler, FreeAgentBillReconciler>();
        services.AddTransient<IFreeAgentAttachmentUploader, FreeAgentAttachmentUploader>();
        services.AddTransient<IFreeAgentGuessRemover, FreeAgentGuessRemover>();

        return services;
    }
}
