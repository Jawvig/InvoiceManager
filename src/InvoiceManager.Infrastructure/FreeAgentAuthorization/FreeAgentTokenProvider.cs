using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace InvoiceManager.Infrastructure.FreeAgentAuthorization;

/// <summary>
/// Refreshes a FreeAgent access token from the stored rotating refresh token.
/// FreeAgent's OAuth is a plain rotating refresh token (not MSAL), so this is a
/// small standalone implementation rather than built on MSAL. Caches the access
/// token in memory for the process lifetime (bounded by <see cref="TokenResponseWire.ExpiresIn"/>);
/// a <see cref="SemaphoreSlim"/> serialises concurrent refreshes so only one HTTP
/// call is made when multiple callers race. Persists the rotated refresh token
/// before returning the new access token to the caller.
/// </summary>
public sealed class FreeAgentTokenProvider : IFreeAgentTokenProvider
{
    private static readonly TimeSpan ExpiryBuffer = TimeSpan.FromMinutes(2);

    private readonly HttpClient httpClient;
    private readonly IFreeAgentAuthorizationStore authorizationStore;
    private readonly IOptions<FreeAgentOptions> freeAgentOptions;
    private readonly IOptions<FreeAgentAuthorizationOptions> authorizationOptions;
    private readonly SemaphoreSlim gate = new(1, 1);

    private string? cachedAccessToken;
    private DateTimeOffset cachedAccessTokenExpiresAt = DateTimeOffset.MinValue;

    public FreeAgentTokenProvider(
        HttpClient httpClient,
        IFreeAgentAuthorizationStore authorizationStore,
        IOptions<FreeAgentOptions> freeAgentOptions,
        IOptions<FreeAgentAuthorizationOptions> authorizationOptions)
    {
        this.httpClient = httpClient;
        this.authorizationStore = authorizationStore;
        this.freeAgentOptions = freeAgentOptions;
        this.authorizationOptions = authorizationOptions;
    }

    public async Task<string> AcquireTokenAsync(CancellationToken cancellationToken = default)
    {
        if (cachedAccessToken is { } cached && DateTimeOffset.UtcNow < cachedAccessTokenExpiresAt)
            return cached;

        await gate.WaitAsync(cancellationToken);
        try
        {
            // Re-check after acquiring the gate: another caller may have already refreshed
            // while this one was waiting.
            if (cachedAccessToken is { } stillCached && DateTimeOffset.UtcNow < cachedAccessTokenExpiresAt)
                return stillCached;

            // The gate above only serialises callers within this process; the refresh token
            // itself rotates in Key Vault and is shared by every Functions instance, so a
            // concurrent refresh from another cold instance can consume it first. Retry once
            // against whatever token is currently stored before giving up, rather than failing
            // outright on the first invalid_grant.
            try
            {
                return await RefreshAsync(cancellationToken);
            }
            catch (InvalidOperationException)
            {
                return await RefreshAsync(cancellationToken);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<string> RefreshAsync(CancellationToken cancellationToken)
    {
        var refreshToken = await authorizationStore.ReadRefreshTokenAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "No FreeAgent refresh token is stored. An administrator must complete FreeAgent " +
                "authorization on the AdminWeb Authorization page before the workflow can call FreeAgent.");

        var environment = freeAgentOptions.Value.Environment;
        var options = authorizationOptions.Value;

        using var request = new HttpRequestMessage(HttpMethod.Post, FreeAgentHosts.TokenEndpoint(environment))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = options.ClientId ?? string.Empty,
                ["client_secret"] = options.ClientSecret ?? string.Empty,
            }),
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Never include the response body: it can echo request parameters.
            throw new InvalidOperationException(
                $"FreeAgent token refresh failed: {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponseWire>(cancellationToken)
            ?? throw new InvalidOperationException("FreeAgent's token response could not be parsed.");

        if (string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            throw new InvalidOperationException("FreeAgent's token response did not include an access token.");

        // Persist the rotated refresh token before exposing the new access token, so a
        // crash between the two never strands the stored token as already-consumed.
        if (!string.IsNullOrWhiteSpace(tokenResponse.RefreshToken))
            await authorizationStore.SaveRefreshTokenAsync(tokenResponse.RefreshToken, cancellationToken);

        cachedAccessToken = tokenResponse.AccessToken;
        cachedAccessTokenExpiresAt = DateTimeOffset.UtcNow
            + TimeSpan.FromSeconds(Math.Max(0, tokenResponse.ExpiresIn - ExpiryBuffer.TotalSeconds));

        return cachedAccessToken;
    }

    private sealed class TokenResponseWire
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; } = 3600;
    }
}
