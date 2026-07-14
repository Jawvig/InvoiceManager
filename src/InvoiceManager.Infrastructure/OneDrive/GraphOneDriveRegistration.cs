using InvoiceManager.Core.Integrations;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceManager.Infrastructure.OneDrive;

/// <summary>
/// Registers <see cref="GraphOneDriveIntegration"/> as the <see cref="IOneDriveIntegration"/>
/// with a typed <see cref="HttpClient"/> whose pipeline has the standard resilience
/// handler: transient-fault and throttling (HTTP 429/503) retry that honours the
/// <c>Retry-After</c> header, plus per-attempt and total-request timeouts. This
/// replaces any bespoke retry loop in the integration itself.
/// </summary>
public static class GraphOneDriveRegistration
{
    public static IHttpClientBuilder AddGraphOneDriveIntegration(this IServiceCollection services)
    {
        var builder = services.AddHttpClient<GraphOneDriveIntegration>();
        builder.AddStandardResilienceHandler();
        services.AddTransient<IOneDriveIntegration>(sp => sp.GetRequiredService<GraphOneDriveIntegration>());
        return builder;
    }
}
