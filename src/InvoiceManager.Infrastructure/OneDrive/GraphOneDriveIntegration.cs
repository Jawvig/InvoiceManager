using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using InvoiceManager.Core;
using InvoiceManager.Core.Integrations;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;

namespace InvoiceManager.Infrastructure.OneDrive;

/// <summary>
/// Uploads invoice PDFs to OneDrive via the Microsoft Graph API, authenticating
/// with a delegated token from the shared MSAL token cache. Invoices are small,
/// so a single simple (non-resumable) upload is used.
/// </summary>
public sealed class GraphOneDriveIntegration(
    HttpClient httpClient,
    IMicrosoftTokenProvider tokenProvider) : IOneDriveIntegration
{
    private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";

    private static readonly string[] Scopes = ["https://graph.microsoft.com/Files.ReadWrite.All"];

    public async Task<OneDriveDetails> UploadAsync(
        OneDriveUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        var token = await tokenProvider.AcquireTokenAsync(Scopes, cancellationToken);

        // Graph simple upload: PUT {drivePath}/{filename}:/content. The destination
        // path already ends at the folder (e.g. /drives/{id}/root:/Bills/Microsoft 365).
        var uploadUrl =
            $"{GraphBaseUrl}{request.DestinationPath}/{Uri.EscapeDataString(request.FileName)}:/content";

        using var message = new HttpRequestMessage(HttpMethod.Put, uploadUrl);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Content = new ByteArrayContent(request.Content);
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"OneDrive upload of '{request.FileName}' failed with {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
        }

        var item = await response.Content.ReadFromJsonAsync<DriveItemResponse>(cancellationToken);
        var location = item?.WebUrl ?? item?.Id
            ?? throw new InvalidOperationException(
                $"OneDrive upload of '{request.FileName}' succeeded but returned no item location.");

        return new OneDriveDetails(location);
    }

    private sealed record DriveItemResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("webUrl")] string? WebUrl);
}
