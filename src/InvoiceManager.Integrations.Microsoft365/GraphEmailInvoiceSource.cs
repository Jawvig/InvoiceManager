using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using InvoiceManager.Core;
using InvoiceManager.Core.Integrations;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using Microsoft.Extensions.Logging;

namespace InvoiceManager.Integrations.Microsoft365;

/// <summary>
/// Retrieves invoices carried as PDF attachments on recurring emails, via the
/// same delegated Microsoft Graph mailbox used for OneDrive uploads. Candidate
/// emails are found by sender address, a date window around the expected
/// invoice date, and an optional body regex; the winning PDF attachment's
/// invoice date and total are then read via <see cref="IInvoicePdfExtractor"/>
/// (VAT mode is never derived here — it always comes from configuration).
/// </summary>
public sealed class GraphEmailInvoiceSource(
    HttpClient httpClient,
    IMicrosoftTokenProvider tokenProvider,
    IInvoicePdfExtractor pdfExtractor,
    ILogger<GraphEmailInvoiceSource> logger) : IInvoiceSourceIntegration
{
    private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";
    private static readonly string[] Scopes = ["https://graph.microsoft.com/Mail.Read"];
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    public IntegrationType IntegrationType => IntegrationType.Microsoft365Email;

    public async Task<InvoiceSourceResult> FindInvoiceAsync(
        InvoiceSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("find_invoice.microsoft365email");
        activity?.SetTag("invoice.sender_email_address", criteria.SenderEmailAddress);
        activity?.SetTag("invoice.expected_date", criteria.ExpectedDate.ToString("O"));
        activity?.SetTag("invoice.date_tolerance_days", criteria.DateToleranceDays);

        var token = await tokenProvider.AcquireTokenAsync(Scopes, cancellationToken);

        var candidates = await ListCandidateMessagesAsync(criteria, token, cancellationToken);
        activity?.SetTag("invoice.candidate_count", candidates.Count);

        // Closest to the expected date first, mirroring the closest-match preference
        // used by the other source/OneDrive matchers.
        foreach (var message in candidates.OrderBy(m => criteria.DateDistanceDays(DateOnly.FromDateTime(m.ReceivedDateTime.UtcDateTime))))
        {
            var attachments = await ListPdfAttachmentsAsync(message.Id, token, cancellationToken);
            if (attachments.Count == 0)
            {
                activity?.AddEvent(new ActivityEvent("matched_email_has_no_pdf"));
                logger.LogWarning(
                    "Email {MessageId} from {Sender} matched criteria but has no PDF attachment; skipping.",
                    message.Id, criteria.SenderEmailAddress);
                continue;
            }

            var extracted = await ExtractInvoiceAsync(message, attachments, token, cancellationToken);
            if (extracted is null)
                continue;

            activity?.SetTag("invoice.matched_message_id", message.Id);
            activity?.AddEvent(new ActivityEvent("match_selected"));
            logger.LogInformation(
                "Retrieved Microsoft365Email invoice from message {MessageId} ({PdfBytes} bytes).",
                message.Id, extracted.Value.PdfContent.Length);

            var details = new ActualInvoiceDetails(
                extracted.Value.Extraction.InvoiceDate,
                extracted.Value.Extraction.Total,
                new SourceInvoiceId(message.Id));

            return new InvoiceMatch(extracted.Value.PdfContent, details);
        }

        activity?.AddEvent(new ActivityEvent("no_match"));
        logger.LogInformation(
            "No email matched criteria for sender {Sender} around {ExpectedDate} ({CandidateCount} candidate(s) considered).",
            criteria.SenderEmailAddress, criteria.ExpectedDate, candidates.Count);
        return new NoInvoiceMatch();
    }

    /// <summary>
    /// Extracts invoice fields from the message's PDF attachment(s). A single PDF
    /// must extract successfully or the failure is thrown (mapped by the caller's
    /// caller to <c>RetrievalError</c>). With more than one PDF, each is attempted
    /// and the first that extracts successfully wins; if none do, that is also
    /// thrown as a failure rather than silently treated as no match, since the
    /// email itself already satisfied the match criteria.
    /// </summary>
    private async Task<(byte[] PdfContent, PdfExtractionSucceeded Extraction)?> ExtractInvoiceAsync(
        GraphMessage message,
        IReadOnlyList<GraphAttachment> pdfAttachments,
        string token,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();

        foreach (var attachment in pdfAttachments)
        {
            var content = await GetAttachmentContentAsync(message.Id, attachment, token, cancellationToken);
            var result = await pdfExtractor.ExtractAsync(content, cancellationToken);

            if (result is PdfExtractionSucceeded succeeded)
                return (content, succeeded);

            var reason = result is PdfExtractionFailed failed ? failed.Reason : "unknown extraction failure";
            failures.Add($"{attachment.Name}: {reason}");
        }

        throw new InvalidOperationException(
            $"Message {message.Id} has {pdfAttachments.Count} PDF attachment(s) but none could be read as an " +
            $"invoice: {string.Join("; ", failures)}.");
    }

    private async Task<IReadOnlyList<GraphMessage>> ListCandidateMessagesAsync(
        InvoiceSearchCriteria criteria,
        string token,
        CancellationToken cancellationToken)
    {
        var windowStart = criteria.ExpectedDate.AddDays(-criteria.DateToleranceDays).ToDateTime(TimeOnly.MinValue);
        var windowEnd = criteria.ExpectedDate.AddDays(criteria.DateToleranceDays).ToDateTime(TimeOnly.MaxValue);

        var filter =
            $"receivedDateTime ge {windowStart:yyyy-MM-ddTHH:mm:ssZ} " +
            $"and receivedDateTime le {windowEnd:yyyy-MM-ddTHH:mm:ssZ} " +
            $"and from/emailAddress/address eq '{criteria.SenderEmailAddress}' " +
            $"and hasAttachments eq true";

        string? url =
            $"{GraphBaseUrl}/me/messages" +
            $"?$filter={Uri.EscapeDataString(filter)}" +
            $"&$select=id,receivedDateTime,body" +
            $"&$orderby=receivedDateTime";

        var messages = new List<GraphMessage>();
        while (url is not null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            // Plain-text body, not raw HTML, so a configured bodyPattern regex can match
            // clean text rather than markup — and not the 255-character bodyPreview.
            request.Headers.Add("Prefer", "outlook.body-content-type=\"text\"");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, "listing candidate emails", cancellationToken);

            var page = await response.Content.ReadFromJsonAsync<GraphMessageListResponse>(cancellationToken);
            foreach (var message in page?.Value ?? [])
            {
                if (MatchesBodyPattern(message, criteria.BodyPattern))
                    messages.Add(message);
            }

            url = page?.NextLink;
        }

        return messages;
    }

    private bool MatchesBodyPattern(GraphMessage message, string bodyPattern)
    {
        if (string.IsNullOrEmpty(bodyPattern))
            return true;

        var body = message.Body?.Content ?? string.Empty;
        try
        {
            return Regex.IsMatch(body, bodyPattern, RegexOptions.None, RegexTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            logger.LogWarning("Body pattern match timed out for message {MessageId}; treating as no match.", message.Id);
            return false;
        }
    }

    private async Task<IReadOnlyList<GraphAttachment>> ListPdfAttachmentsAsync(
        string messageId, string token, CancellationToken cancellationToken)
    {
        var url = $"{GraphBaseUrl}/me/messages/{messageId}/attachments" +
            "?$select=id,name,contentType,contentBytes,isInline";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "listing message attachments", cancellationToken);

        var page = await response.Content.ReadFromJsonAsync<GraphAttachmentListResponse>(cancellationToken);
        return (page?.Value ?? [])
            .Where(a => !a.IsInline && string.Equals(a.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private async Task<byte[]> GetAttachmentContentAsync(
        string messageId, GraphAttachment attachment, string token, CancellationToken cancellationToken)
    {
        // Small attachments are inlined as base64 on the listing call; larger ones omit
        // contentBytes and must be fetched separately via $value.
        if (attachment.ContentBytes is { Length: > 0 } base64)
            return Convert.FromBase64String(base64);

        var url = $"{GraphBaseUrl}/me/messages/{messageId}/attachments/{attachment.Id}/$value";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "downloading attachment content", cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"Microsoft Graph request failed while {action}: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
    }

    private sealed record GraphMessageListResponse(
        [property: JsonPropertyName("value")] IReadOnlyList<GraphMessage>? Value,
        [property: JsonPropertyName("@odata.nextLink")] string? NextLink);

    private sealed record GraphMessage(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("receivedDateTime")] DateTimeOffset ReceivedDateTime,
        [property: JsonPropertyName("body")] GraphBody? Body);

    private sealed record GraphBody(
        [property: JsonPropertyName("content")] string? Content);

    private sealed record GraphAttachmentListResponse(
        [property: JsonPropertyName("value")] IReadOnlyList<GraphAttachment>? Value);

    private sealed record GraphAttachment(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("contentType")] string? ContentType,
        [property: JsonPropertyName("contentBytes")] string? ContentBytes,
        [property: JsonPropertyName("isInline")] bool IsInline);
}
