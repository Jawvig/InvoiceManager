using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using InvoiceManager.Core;
using InvoiceManager.Core.Integrations;
using InvoiceManager.Infrastructure.Http;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace InvoiceManager.Infrastructure.OneDrive;

/// <summary>
/// Uploads invoice PDFs to OneDrive and reconciles against files already present
/// there, via the Microsoft Graph API and a delegated token from the shared MSAL
/// token cache. Invoices are small, so a single simple (non-resumable) upload is
/// used. Transient-fault and throttling (HTTP 429/503, honouring <c>Retry-After</c>)
/// handling is applied to this client's HTTP pipeline by the standard resilience
/// handler wired up in <see cref="GraphOneDriveRegistration"/>, not here.
/// </summary>
public sealed class GraphOneDriveIntegration(
    HttpClient httpClient,
    IMicrosoftTokenProvider tokenProvider,
    InvoiceFilename invoiceFilename,
    ILogger<GraphOneDriveIntegration>? logger = null) : IOneDriveIntegration
{
    private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";

    private static readonly string[] Scopes = ["https://graph.microsoft.com/Files.ReadWrite.All"];

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

        using var message = new HttpRequestMessage(HttpMethod.Put, uploadUrl);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Content = new ByteArrayContent(request.Content);
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

        using var response = await httpClient.SendAsync(message, cancellationToken);
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
            using var message = new HttpRequestMessage(HttpMethod.Get, next);
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await httpClient.SendAsync(message, cancellationToken);

            // A missing destination folder (Graph returns 404 itemNotFound) means no invoice
            // has ever been written there. The upload path silently creates the folder on the
            // first save, so a folder that doesn't exist yet is simply "nothing to reconcile
            // against" — a warning and a no-match, not a retrieval failure that would strand
            // the record in RetrievalError.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                activity?.AddEvent(new ActivityEvent("destination_not_found"));
                logger.LogWarning(
                    "OneDrive destination folder {Destination} does not exist yet; treating as no match. " +
                    "It will be created when an invoice is first saved there.",
                    request.DestinationPath);
                return new NoOneDriveMatch();
            }

            await response.EnsureSuccessAsync("Microsoft Graph", "listing OneDrive files", cancellationToken);

            var page = await response.Content.ReadFromJsonAsync<DriveChildrenResponse>(cancellationToken);
            foreach (var child in page?.Value ?? [])
            {
                if (child.Name is null || !invoiceFilename.TryParse(child.Name, out var parsed) || parsed is null)
                    continue;

                candidateCount++;
                if (!request.Criteria.Matches(parsed.InvoiceDate, parsed.Amount, parsed.InvoiceDescription))
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
            $"(within {request.Criteria.DateToleranceDays}d)" +
            (request.Criteria.AmountMatchingCriteria is AmountMatchingCriteria
                ? $" and amount {bestParsed.Amount}."
                : ".");

        activity?.SetTag("onedrive.matched_name", best.Name);
        activity?.AddEvent(new ActivityEvent("match_selected"));
        logger.LogInformation("Reconciled record against existing OneDrive file '{FileName}'.", best.Name);

        var details = new ActualInvoiceDetails(
            bestParsed.InvoiceDate,
            bestParsed.Amount,
            new SourceInvoiceId(bestParsed.InvoiceName));

        return new OneDriveMatch(new OneDriveDetails(location), details, matchReason);
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
