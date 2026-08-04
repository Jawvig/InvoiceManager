namespace InvoiceManager.Infrastructure.FreeAgentAuthorization;

public interface IFreeAgentAuthorizationStore
{
    Task<bool> HasRefreshTokenAsync(CancellationToken cancellationToken = default);

    Task<string?> ReadRefreshTokenAsync(CancellationToken cancellationToken = default);

    Task SaveRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task ClearRefreshTokenAsync(CancellationToken cancellationToken = default);
}
