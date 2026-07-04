namespace InvoiceManager.Infrastructure.MicrosoftAuthorization;

/// <summary>
/// Acquires access tokens for downstream Microsoft APIs (for example the Azure
/// Billing API or Microsoft Graph) using the delegated MSAL token cache that the
/// admin website persisted to Key Vault.
/// </summary>
public interface IMicrosoftTokenProvider
{
    /// <summary>
    /// Silently acquires an access token for the requested <paramref name="scopes"/>
    /// from the cached delegated account. Throws if no account has been signed in
    /// or if the cached refresh token can no longer satisfy the request.
    /// </summary>
    Task<string> AcquireTokenAsync(IReadOnlyCollection<string> scopes, CancellationToken cancellationToken = default);
}
