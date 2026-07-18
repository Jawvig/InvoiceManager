namespace InvoiceManager.Infrastructure.Http;

/// <summary>
/// Shared success-check for the HTTP clients in this repo's integrations: throws with
/// the response body attached when the call did not succeed, so callers get one
/// consistent failure message shape instead of each integration re-implementing it.
/// </summary>
public static class HttpResponseMessageExtensions
{
    /// <param name="sourceLabel">The system the request was made to, e.g. "Microsoft Graph" or "Microsoft 365 billing".</param>
    /// <param name="action">What the request was doing, e.g. "listing invoices".</param>
    public static async Task EnsureSuccessAsync(
        this HttpResponseMessage response, string sourceLabel, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"{sourceLabel} request failed while {action}: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
    }
}
