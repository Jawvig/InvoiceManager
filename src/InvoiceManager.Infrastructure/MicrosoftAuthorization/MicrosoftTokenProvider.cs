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
    private readonly IConfidentialClientApplication application;

    public MicrosoftTokenProvider(
        IOptions<MicrosoftAuthorizationOptions> options,
        IMicrosoftAuthorizationStore authorizationStore)
    {
        var authorizationOptions = options.Value;

        application = ConfidentialClientApplicationBuilder
            .Create(authorizationOptions.ClientId)
            .WithClientSecret(authorizationOptions.ClientSecret)
            .WithAuthority(authorizationOptions.Authority)
            .Build();

        MsalTokenCacheBinding.Bind(application.UserTokenCache, authorizationStore);
    }

    public async Task<string> AcquireTokenAsync(
        IReadOnlyCollection<string> scopes,
        CancellationToken cancellationToken = default)
    {
        var accounts = await application.GetAccountsAsync();
        var account = accounts.FirstOrDefault()
            ?? throw new InvalidOperationException(
                "No delegated account is available in the MSAL token cache. An administrator " +
                "must sign in through the admin website before invoices can be retrieved.");

        var result = await application
            .AcquireTokenSilent(scopes, account)
            .ExecuteAsync(cancellationToken);

        return result.AccessToken;
    }
}
