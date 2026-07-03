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

    public MicrosoftTokenProvider(
        IOptions<MicrosoftAuthorizationOptions> options,
        IMicrosoftAuthorizationStore authorizationStore)
    {
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
        var accounts = await application.Value.GetAccountsAsync();
        var account = accounts.FirstOrDefault()
            ?? throw new InvalidOperationException(
                "No delegated account is available in the MSAL token cache. An administrator " +
                "must sign in through the admin website before invoices can be retrieved.");

        var result = await application.Value
            .AcquireTokenSilent(scopes, account)
            .ExecuteAsync(cancellationToken);

        return result.AccessToken;
    }
}
