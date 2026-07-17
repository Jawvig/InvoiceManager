using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using InvoiceManager.Core;
using InvoiceManager.Core.Integrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaMoney;

namespace InvoiceManager.Infrastructure.DocumentIntelligence;

/// <summary>
/// Reads invoice date and total out of a PDF using Azure AI Document
/// Intelligence's prebuilt <c>invoice</c> model — structured fields with
/// confidence scores, authenticated via the Functions app's managed identity
/// (app-only; unrelated to the delegated MSAL cache used for Graph/Billing).
/// VAT mode is never derived from the result: Document Intelligence's invoice
/// model has no inclusive/exclusive field, and this repo's rule is that VAT
/// mode always comes from configuration regardless of source.
/// </summary>
public sealed class DocumentIntelligencePdfExtractor(
    HttpClient httpClient,
    TokenCredential credential,
    IOptions<DocumentIntelligenceOptions> options,
    ILogger<DocumentIntelligencePdfExtractor> logger) : IInvoicePdfExtractor
{
    private static readonly string[] Scopes = ["https://cognitiveservices.azure.com/.default"];

    private readonly DocumentIntelligenceOptions settings = options.Value;

    public async Task<PdfExtractionResult> ExtractAsync(byte[] pdfContent, CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("extract_invoice_pdf");
        activity?.SetTag("pdf.bytes", pdfContent.Length);

        var token = await credential.GetTokenAsync(new TokenRequestContext(Scopes), cancellationToken);

        var endpoint = settings.Endpoint
            ?? throw new InvalidOperationException("DocumentIntelligence:Endpoint is not configured.");
        var analyzeUrl =
            $"{endpoint.AbsoluteUri.TrimEnd('/')}/documentintelligence/documentModels/{settings.ModelId}:analyze" +
            $"?api-version={settings.ApiVersion}";

        using var analyzeRequest = new HttpRequestMessage(HttpMethod.Post, analyzeUrl)
        {
            Content = new ByteArrayContent(pdfContent),
        };
        analyzeRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        analyzeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        using var analyzeResponse = await httpClient.SendAsync(analyzeRequest, cancellationToken);
        if (analyzeResponse.StatusCode != HttpStatusCode.Accepted)
            return await FailedAsync(analyzeResponse, "starting document analysis", activity, cancellationToken);

        var operationLocation = analyzeResponse.Headers.Location
            ?? throw new InvalidOperationException("Document analysis did not return an Operation-Location header.");

        var result = await PollUntilCompleteAsync(operationLocation, token.Token, activity, cancellationToken);
        return result;
    }

    private async Task<PdfExtractionResult> PollUntilCompleteAsync(
        Uri operationLocation, string token, Activity? activity, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + settings.MaxPollDuration;

        while (true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, operationLocation);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return await FailedAsync(response, "polling document analysis", activity, cancellationToken);

            var body = await response.Content.ReadFromJsonAsync<AnalyzeOperationResult>(cancellationToken: cancellationToken);

            if (body?.Status == "succeeded")
                return ParseResult(body, activity);

            if (body?.Status == "failed")
            {
                var reason = body.Error?.Message ?? "Document Intelligence reported analysis failure with no error detail.";
                logger.LogWarning("Document Intelligence analysis failed: {Reason}", reason);
                return new PdfExtractionFailed(reason);
            }

            var delay = response.Headers.RetryAfter?.Delta ?? settings.PollInterval;
            if (DateTimeOffset.UtcNow + delay > deadline)
            {
                logger.LogWarning(
                    "Document Intelligence analysis did not complete within {MaxPollDuration}; giving up.",
                    settings.MaxPollDuration);
                return new PdfExtractionFailed(
                    $"Document analysis did not complete within {settings.MaxPollDuration}.");
            }

            await Task.Delay(delay, cancellationToken);
        }
    }

    private PdfExtractionResult ParseResult(AnalyzeOperationResult body, Activity? activity)
    {
        var document = body.AnalyzeResult?.Documents?.FirstOrDefault();
        if (document is null)
            return new PdfExtractionFailed("Document Intelligence returned no analyzed document.");

        var fields = document.Fields;

        if (fields is null ||
            !fields.TryGetValue("InvoiceDate", out var dateField) ||
            dateField.ValueDate is not { } dateValue ||
            (dateField.Confidence ?? 0) < settings.MinimumFieldConfidence)
        {
            return new PdfExtractionFailed(
                $"InvoiceDate field missing or below the confidence threshold ({settings.MinimumFieldConfidence:P0}).");
        }

        if (!fields.TryGetValue("InvoiceTotal", out var totalField) ||
            totalField.ValueCurrency is not { } currencyValue ||
            (totalField.Confidence ?? 0) < settings.MinimumFieldConfidence)
        {
            return new PdfExtractionFailed(
                $"InvoiceTotal field missing or below the confidence threshold ({settings.MinimumFieldConfidence:P0}).");
        }

        if (string.IsNullOrWhiteSpace(currencyValue.CurrencyCode))
            return new PdfExtractionFailed("InvoiceTotal field has no currency code.");

        activity?.SetTag("invoice.extracted_date", dateValue.ToString("O"));
        activity?.SetTag("invoice.extracted_amount", currencyValue.Amount);
        activity?.SetTag("invoice.extracted_currency", currencyValue.CurrencyCode);

        return new PdfExtractionSucceeded(
            DateOnly.FromDateTime(dateValue),
            new Money(currencyValue.Amount, currencyValue.CurrencyCode));
    }

    private async Task<PdfExtractionResult> FailedAsync(
        HttpResponseMessage response, string action, Activity? activity, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var reason = $"Document Intelligence request failed while {action}: {(int)response.StatusCode} {response.ReasonPhrase}. {body}";
        activity?.SetStatus(ActivityStatusCode.Error, reason);
        logger.LogWarning("{Reason}", reason);
        return new PdfExtractionFailed(reason);
    }

    private sealed record AnalyzeOperationResult(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("analyzeResult")] AnalyzeResult? AnalyzeResult,
        [property: JsonPropertyName("error")] AnalyzeError? Error);

    private sealed record AnalyzeError(
        [property: JsonPropertyName("message")] string? Message);

    private sealed record AnalyzeResult(
        [property: JsonPropertyName("documents")] IReadOnlyList<AnalyzedDocument>? Documents);

    private sealed record AnalyzedDocument(
        [property: JsonPropertyName("fields")] IReadOnlyDictionary<string, AnalyzedField>? Fields);

    private sealed record AnalyzedField(
        [property: JsonPropertyName("valueDate")] DateTime? ValueDate,
        [property: JsonPropertyName("valueCurrency")] AnalyzedCurrency? ValueCurrency,
        [property: JsonPropertyName("confidence")] double? Confidence);

    private sealed record AnalyzedCurrency(
        [property: JsonPropertyName("amount")] decimal Amount,
        [property: JsonPropertyName("currencyCode")] string? CurrencyCode);
}
