namespace InvoiceManager.Infrastructure.FreeAgentAuthorization;

/// <summary>Acquires a FreeAgent API bearer access token from the stored rotating refresh token.</summary>
public interface IFreeAgentTokenProvider
{
    Task<string> AcquireTokenAsync(CancellationToken cancellationToken = default);
}
