namespace InvoiceManager.Infrastructure.MicrosoftAuthorization;

public interface IMicrosoftAuthorizationStore
{
    Task<bool> HasTokenCacheAsync(CancellationToken cancellationToken = default);

    Task<byte[]?> ReadTokenCacheAsync(CancellationToken cancellationToken = default);

    Task SaveTokenCacheAsync(byte[] tokenCache, CancellationToken cancellationToken = default);

    Task ClearTokenCacheAsync(CancellationToken cancellationToken = default);
}
