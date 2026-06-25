using Microsoft.Extensions.Options;

namespace InvoiceManager.Infrastructure.MicrosoftAuthorization;

public sealed class KeyVaultMicrosoftAuthorizationStore : IMicrosoftAuthorizationStore
{
    private readonly ISecretStoreClient secretStoreClient;
    private readonly string secretName;

    public KeyVaultMicrosoftAuthorizationStore(
        ISecretStoreClient secretStoreClient,
        IOptions<MicrosoftAuthorizationOptions> options)
    {
        this.secretStoreClient = secretStoreClient;
        secretName = options.Value.TokenCacheSecretName;
    }

    public async Task<bool> HasTokenCacheAsync(CancellationToken cancellationToken = default)
    {
        var tokenCache = await ReadTokenCacheAsync(cancellationToken);
        return tokenCache is { Length: > 0 };
    }

    public async Task<byte[]?> ReadTokenCacheAsync(CancellationToken cancellationToken = default)
    {
        var encodedTokenCache = await secretStoreClient.GetSecretAsync(secretName, cancellationToken);
        if (string.IsNullOrWhiteSpace(encodedTokenCache))
        {
            return null;
        }

        return Convert.FromBase64String(encodedTokenCache);
    }

    public Task SaveTokenCacheAsync(byte[] tokenCache, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokenCache);

        if (tokenCache.Length == 0)
        {
            throw new ArgumentException("Token cache payload cannot be empty.", nameof(tokenCache));
        }

        return secretStoreClient.SetSecretAsync(
            secretName,
            Convert.ToBase64String(tokenCache),
            cancellationToken);
    }

    public Task ClearTokenCacheAsync(CancellationToken cancellationToken = default)
    {
        return secretStoreClient.DeleteSecretAsync(secretName, cancellationToken);
    }
}
