using System.Diagnostics;
using InvoiceManager.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace InvoiceManager.Infrastructure.MicrosoftAuthorization;

/// <summary>
/// Acquires delegated access tokens using a <see cref="IConfidentialClientApplication"/>
/// whose user token cache is backed by the MSAL cache the admin website stored in
/// Key Vault. The cache is re-read from the store on every acquisition, so a token
/// refreshed in one process is visible to the others.
/// </summary>
public sealed class MicrosoftTokenProvider : IMicrosoftTokenProvider
{
    private readonly Lazy<IConfidentialClientApplication> application;
    private readonly ILogger<MicrosoftTokenProvider> logger;

    public MicrosoftTokenProvider(
        IOptions<MicrosoftAuthorizationOptions> options,
        IMicrosoftAuthorizationStore authorizationStore,
        ILogger<MicrosoftTokenProvider>? logger = null)
    {
        this.logger = logger ?? NullLogger<MicrosoftTokenProvider>.Instance;

        // Build the MSAL application lazily so that constructing the provider never
        // does work or throws on missing configuration; misconfiguration surfaces
        // only when a token is actually requested.
        application = new Lazy<IConfidentialClientApplication>(() =>
        {
            var authorizationOptions = options.Value;

            var app = ConfidentialClientApplicationBuilder
                .Create(authorizationOptions.ClientId)
                .WithClientSecret(authorizationOptions.ClientSecret)
                .WithAuthority(authorizationOptions.Authority)
                .Build();

            MsalTokenCacheBinding.Bind(app.UserTokenCache, authorizationStore);
            return app;
        });
    }

    public async Task<string> AcquireTokenAsync(
        IReadOnlyCollection<string> scopes,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("acquire_token");
        activity?.SetTag("auth.scopes", string.Join(' ', scopes));

        var accounts = await application.Value.GetAccountsAsync();
        var account = accounts.FirstOrDefault();
        if (account is null)
        {
            // Retrieval cannot proceed without a delegated account: record why so an
            // operator sees the missing sign-in rather than an opaque failure.
            const string message =
                "No delegated account is available in the MSAL token cache. An administrator " +
                "must sign in through the admin website before invoices can be retrieved.";
            activity?.SetStatus(ActivityStatusCode.Error, message);
            logger.LogWarning(
                "Token acquisition for scopes {Scopes} failed: no delegated account in the MSAL cache.",
                string.Join(' ', scopes));
            throw new InvalidOperationException(message);
        }

        var result = await application.Value
            .AcquireTokenSilent(scopes, account)
            .ExecuteAsync(cancellationToken);

        logger.LogDebug("Acquired delegated token for scopes {Scopes}.", string.Join(' ', scopes));
        return result.AccessToken;
    }
}
