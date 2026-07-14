using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using InvoiceManager.Core;
using InvoiceManager.Core.Integrations;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace InvoiceManager.Infrastructure.OneDrive;

/// <summary>
/// Uploads invoice PDFs to OneDrive and reconciles against files already present
/// there, via the Microsoft Graph API and a delegated token from the shared MSAL
/// token cache. Invoices are small, so a single simple (non-resumable) upload is
/// used. Every Graph call honours the <c>Retry-After</c> header on throttling
/// (HTTP 429/503) responses and retries a bounded number of times.
/// </summary>
public sealed class GraphOneDriveIntegration(
    HttpClient httpClient,
    IMicrosoftTokenProvider tokenProvider,
    InvoiceFilename invoiceFilename,
    TimeProvider? timeProvider = null,
    ILogger<GraphOneDriveIntegration>? logger = null) : IOneDriveIntegration
{
    private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";
    private const int MaxThrottleAttempts = 5;

    private static readonly string[] Scopes = ["https://graph.microsoft.com/Files.ReadWrite.All"];
    private static readonly TimeSpan DefaultThrottleDelay = TimeSpan.FromSeconds(2);

    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ILogger<GraphOneDriveIntegration> logger =
        logger ?? NullLogger<GraphOneDriveIntegration>.Instance;

    public async Task<OneDriveDetails> UploadAsync(
        OneDriveUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("upload_onedrive");
        activity?.SetTag("onedrive.file_name", request.FileName);
        activity?.SetTag("onedrive.destination", request.DestinationPath);

        var token = await tokenProvider.AcquireTokenAsync(Scopes, cancellationToken);

        // Graph simple upload: PUT {drivePath}/{filename}:/content. The destination
        // path already ends at the folder (e.g. /drives/{id}/root:/Bills/Microsoft 365).
        var uploadUrl =
            $"{GraphBaseUrl}{request.DestinationPath}/{Uri.EscapeDataString(request.FileName)}:/content";

        using var response = await SendWithThrottlingRetryAsync(
            () =>
            {
                var message = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
                {
                    Content = new ByteArrayContent(request.Content),
                };
                message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                return message;
            },
            token,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            activity?.SetTag("http.response.status_code", (int)response.StatusCode);
            activity?.SetStatus(ActivityStatusCode.Error, $"OneDrive upload returned {(int)response.StatusCode}.");
            logger.LogError(
                "OneDrive upload of '{FileName}' failed with {StatusCode} {ReasonPhrase}.",
                request.FileName, (int)response.StatusCode, response.ReasonPhrase);
            throw new HttpRequestException(
                $"OneDrive upload of '{request.FileName}' failed with {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
        }

        var item = await response.Content.ReadFromJsonAsync<DriveItemResponse>(cancellationToken);
        var location = item?.WebUrl ?? item?.Id
            ?? throw new InvalidOperationException(
                $"OneDrive upload of '{request.FileName}' succeeded but returned no item location.");

        activity?.SetTag("onedrive.location", location);
        logger.LogInformation(
            "Uploaded '{FileName}' to OneDrive at {Location}.", request.FileName, location);
        return new OneDriveDetails(location);
    }

    public async Task<OneDriveSearchResult> SearchAsync(
        OneDriveSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("search_onedrive");
        activity?.SetTag("onedrive.destination", request.DestinationPath);
        activity?.SetTag("invoice.expected_date", request.Criteria.ExpectedDate.ToString("O"));

        var token = await tokenProvider.AcquireTokenAsync(Scopes, cancellationToken);

        // Graph children listing: GET {folderPath}:/children. The destination path
        // already ends at the folder, so append ":/children".
        var next = $"{GraphBaseUrl}{request.DestinationPath}:/children";
        var candidateCount = 0;
        DriveChild? best = null;
        ParsedInvoiceFilename? bestParsed = null;

        while (next is not null)
        {
            var pageUrl = next;
            using var response = await SendWithThrottlingRetryAsync(
                () => new HttpRequestMessage(HttpMethod.Get, pageUrl), token, cancellationToken);
            await EnsureSuccessAsync(response, "listing OneDrive files", cancellationToken);

            var page = await response.Content.ReadFromJsonAsync<DriveChildrenResponse>(cancellationToken);
            foreach (var child in page?.Value ?? [])
            {
                if (child.Name is null || !invoiceFilename.TryParse(child.Name, out var parsed) || parsed is null)
                    continue;

                candidateCount++;
                if (!request.Criteria.Matches(parsed.InvoiceDate, parsed.Amount))
                    continue;

                // Prefer the candidate whose date is closest to the expected date.
                if (bestParsed is null ||
                    request.Criteria.DateDistanceDays(parsed.InvoiceDate)
                        < request.Criteria.DateDistanceDays(bestParsed.InvoiceDate))
                {
                    best = child;
                    bestParsed = parsed;
                }
            }

            next = page?.NextLink;
        }

        activity?.SetTag("onedrive.candidate_count", candidateCount);

        if (best is null || bestParsed is null)
        {
            activity?.AddEvent(new ActivityEvent("no_match"));
            logger.LogInformation(
                "No OneDrive file matched criteria in {Destination} around {ExpectedDate} " +
                "({CandidateCount} parseable candidate(s) considered).",
                request.DestinationPath, request.Criteria.ExpectedDate, candidateCount);
            return new NoOneDriveMatch();
        }

        var location = best.WebUrl ?? best.Id
            ?? throw new InvalidOperationException(
                $"OneDrive file '{best.Name}' matched but returned no location.");
        var matchReason =
            $"Matched OneDrive file '{best.Name}' by date {bestParsed.InvoiceDate:O} " +
            $"(within {request.Criteria.DateToleranceDays}d) and amount {bestParsed.Amount}.";

        activity?.SetTag("onedrive.matched_name", best.Name);
        activity?.AddEvent(new ActivityEvent("match_selected"));
        logger.LogInformation("Reconciled record against existing OneDrive file '{FileName}'.", best.Name);

        var details = new ActualInvoiceDetails(
            bestParsed.InvoiceDate,
            bestParsed.Amount,
            new SourceInvoiceId(bestParsed.InvoiceName));

        return new OneDriveMatch(new OneDriveDetails(location), details, matchReason);
    }

    /// <summary>
    /// Sends a freshly-built request, retrying on Graph throttling (HTTP 429/503):
    /// it waits the response's <c>Retry-After</c> delay (or a short default) and
    /// retries up to <see cref="MaxThrottleAttempts"/> times. The request is rebuilt
    /// per attempt because its content cannot be re-sent. Delays go through the
    /// injected <see cref="TimeProvider"/> so tests do not really sleep.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithThrottlingRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        string token,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var message = requestFactory();
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await httpClient.SendAsync(message, cancellationToken);

            if (response.StatusCode is not (HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                || attempt >= MaxThrottleAttempts)
            {
                return response;
            }

            var delay = response.Headers.RetryAfter?.Delta ?? DefaultThrottleDelay;
            response.Dispose();

            Activity.Current?.AddEvent(new ActivityEvent("throttled_retry"));
            logger.LogWarning(
                "Graph throttled the request (attempt {Attempt}/{MaxAttempts}); retrying after {Delay}.",
                attempt, MaxThrottleAttempts, delay);

            await Task.Delay(delay, timeProvider, cancellationToken);
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"Microsoft Graph request failed while {action}: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
    }

    private sealed record DriveItemResponse(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("webUrl")] string? WebUrl);

    private sealed record DriveChildrenResponse(
        [property: JsonPropertyName("value")] IReadOnlyList<DriveChild>? Value,
        [property: JsonPropertyName("@odata.nextLink")] string? NextLink);

    private sealed record DriveChild(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("webUrl")] string? WebUrl);
}
