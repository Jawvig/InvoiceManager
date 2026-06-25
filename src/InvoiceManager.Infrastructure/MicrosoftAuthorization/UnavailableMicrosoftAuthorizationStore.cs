namespace InvoiceManager.Infrastructure.MicrosoftAuthorization;

public sealed class UnavailableMicrosoftAuthorizationStore : IMicrosoftAuthorizationStore
{
    public Task<bool> HasTokenCacheAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<byte[]?> ReadTokenCacheAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<byte[]?>(null);
    }

    public Task SaveTokenCacheAsync(byte[] tokenCache, CancellationToken cancellationToken = default)
    {
        throw CreateConfigurationException();
    }

    public Task ClearTokenCacheAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    private static InvalidOperationException CreateConfigurationException()
    {
        return new InvalidOperationException(
            "MicrosoftAuthorization:KeyVaultUri is required before Microsoft authorization can be persisted.");
    }
}
