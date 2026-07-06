using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using InvoiceManager.Core;
using InvoiceManager.Core.Integrations;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NodaMoney;

namespace InvoiceManager.Integrations.Microsoft365;

/// <summary>
/// Retrieves invoices from the Azure Billing REST API for Microsoft 365. Lists
/// invoices in the expected date window, matches on date and amount within the
/// configured tolerances, downloads the matched invoice document (handling the
/// async 202/Location download pattern), and returns PDF bytes — extracting the
/// single PDF from a ZIP where the API returns one.
/// </summary>
public sealed class MicrosoftBillingInvoiceSource(
    HttpClient httpClient,
    IMicrosoftTokenProvider tokenProvider,
    IOptions<MicrosoftBillingOptions> options,
    ILogger<MicrosoftBillingInvoiceSource> logger) : IInvoiceSourceIntegration
{
    private const string BillingBaseUrl = "https://management.azure.com/providers/Microsoft.Billing/billingAccounts";
    private const byte ZipMagic0 = 0x50; // 'P'
    private const byte ZipMagic1 = 0x4B; // 'K'

    private readonly MicrosoftBillingOptions settings = options.Value;

    public IntegrationType IntegrationType => IntegrationType.Microsoft365;

    public async Task<InvoiceSourceResult> FindInvoiceAsync(
        InvoiceSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("find_invoice.microsoft365");
        activity?.SetTag("invoice.billing_account_id", criteria.BillingAccountId);
        activity?.SetTag("invoice.expected_date", criteria.ExpectedDate.ToString("O"));
        activity?.SetTag("invoice.date_tolerance_days", criteria.DateToleranceDays);

        var token = await tokenProvider.AcquireTokenAsync([settings.Scope], cancellationToken);

        var invoices = await ListInvoicesAsync(criteria, token, cancellationToken);
        activity?.SetTag("invoice.candidate_count", invoices.Count);
        if (SelectBestMatch(invoices, criteria) is not BillingInvoice match)
        {
            activity?.AddEvent(new ActivityEvent("no_match"));
            logger.LogInformation(
                "No Microsoft 365 invoice matched criteria for billing account {BillingAccountId} around {ExpectedDate} " +
                "({CandidateCount} candidate(s) considered).",
                criteria.BillingAccountId, criteria.ExpectedDate, invoices.Count);
            return new NoInvoiceMatch();
        }

        activity?.SetTag("invoice.matched_name", match.Name);
        activity?.AddEvent(new ActivityEvent("match_selected"));

        var document = match.Properties.Documents?.FirstOrDefault(d => d.Kind == "Invoice")
            ?? throw new InvalidOperationException(
                $"Microsoft 365 invoice '{match.Name}' matched but has no document of kind 'Invoice'.");

        var downloadUrl = await RequestDownloadUrlAsync(criteria.BillingAccountId, match.Name, document.Name, token, cancellationToken);
        var payload = await DownloadAsync(downloadUrl, cancellationToken);
        var pdf = ExtractPdf(payload);
        logger.LogInformation(
            "Retrieved Microsoft 365 invoice {InvoiceName} ({PdfBytes} bytes) for billing account {BillingAccountId}.",
            match.Name, pdf.Length, criteria.BillingAccountId);

        var details = new ActualInvoiceDetails(
            DateOnly.FromDateTime(match.Properties.InvoiceDate.UtcDateTime),
            new Money(match.Properties.TotalAmount.Value, match.Properties.TotalAmount.Currency),
            new SourceInvoiceId(match.Name));

        return new InvoiceMatch(pdf, details);
    }

    private async Task<IReadOnlyList<BillingInvoice>> ListInvoicesAsync(
        InvoiceSearchCriteria criteria,
        string token,
        CancellationToken cancellationToken)
    {
        var periodStart = criteria.ExpectedDate.AddDays(-criteria.DateToleranceDays);
        var periodEnd = criteria.ExpectedDate.AddDays(criteria.DateToleranceDays);

        var url =
            $"{BillingBaseUrl}/{Uri.EscapeDataString(criteria.BillingAccountId)}/invoices" +
            $"?api-version={settings.ApiVersion}" +
            $"&periodStartDate={periodStart:yyyy-MM-dd}" +
            $"&periodEndDate={periodEnd:yyyy-MM-dd}";

        using var response = await SendAuthenticatedAsync(HttpMethod.Get, url, token, content: null, cancellationToken);
        await EnsureSuccessAsync(response, "listing invoices", cancellationToken);

        var list = await response.Content.ReadFromJsonAsync<BillingInvoiceListResponse>(cancellationToken);
        return list?.Value ?? [];
    }

    private BillingInvoice? SelectBestMatch(IReadOnlyList<BillingInvoice> invoices, InvoiceSearchCriteria criteria)
    {
        var expectedCurrency = criteria.ExpectedAmount.Currency.Code;
        var expectedAmount = criteria.ExpectedAmount.Amount;

        return invoices
            .Where(invoice =>
            {
                var invoiceDate = DateOnly.FromDateTime(invoice.Properties.InvoiceDate.UtcDateTime);
                var amount = invoice.Properties.TotalAmount;
                var dateMatches = Math.Abs(invoiceDate.DayNumber - criteria.ExpectedDate.DayNumber) <= criteria.DateToleranceDays;
                var currencyMatches = string.Equals(amount.Currency, expectedCurrency, StringComparison.OrdinalIgnoreCase);
                var amountMatches = Math.Abs(amount.Value - expectedAmount) <= criteria.AmountTolerance;
                return dateMatches && currencyMatches && amountMatches;
            })
            .OrderBy(invoice =>
                Math.Abs(DateOnly.FromDateTime(invoice.Properties.InvoiceDate.UtcDateTime).DayNumber - criteria.ExpectedDate.DayNumber))
            .FirstOrDefault();
    }

    private async Task<string> RequestDownloadUrlAsync(
        string billingAccountId,
        string invoiceName,
        string documentName,
        string token,
        CancellationToken cancellationToken)
    {
        var url =
            $"{BillingBaseUrl}/{Uri.EscapeDataString(billingAccountId)}/downloadDocuments" +
            $"?api-version={settings.ApiVersion}";

        var requestBody = JsonSerializer.Serialize(new[] { new { invoiceName, documentName } });

        using var response = await SendAuthenticatedAsync(
            HttpMethod.Post, url, token,
            new StringContent(requestBody, Encoding.UTF8, "application/json"),
            cancellationToken);

        var completed = await FollowDownloadOperationAsync(response, token, cancellationToken);
        var result = JsonSerializer.Deserialize<DownloadDocumentsResponse>(completed)
            ?? throw new InvalidOperationException("The document download response could not be parsed.");

        return string.IsNullOrWhiteSpace(result.Url)
            ? throw new InvalidOperationException("The document download response did not include a URL.")
            : result.Url;
    }

    private async Task<string> FollowDownloadOperationAsync(
        HttpResponseMessage initialResponse,
        string token,
        CancellationToken cancellationToken)
    {
        var response = initialResponse;
        var deadline = DateTimeOffset.UtcNow + settings.MaxPollDuration;
        var pollCount = 0;

        while (true)
        {
            if (response.StatusCode == HttpStatusCode.OK)
                return await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode != HttpStatusCode.Accepted)
            {
                await EnsureSuccessAsync(response, "requesting document download", cancellationToken);
            }

            var pollUrl = response.Headers.Location
                ?? throw new InvalidOperationException("A 202 Accepted download response did not include a Location header.");
            var delay = response.Headers.RetryAfter?.Delta ?? settings.PollInterval;

            if (DateTimeOffset.UtcNow + delay > deadline)
            {
                Activity.Current?.SetStatus(ActivityStatusCode.Error, "Download polling exceeded the maximum duration.");
                logger.LogWarning(
                    "Microsoft 365 document download did not complete within {MaxPollDuration} after {PollCount} poll(s); giving up.",
                    settings.MaxPollDuration, pollCount);
                throw new TimeoutException($"The document download did not complete within {settings.MaxPollDuration}.");
            }

            pollCount++;
            logger.LogDebug(
                "Microsoft 365 document download not ready (poll {PollCount}); retrying after {Delay}.",
                pollCount, delay);

            await Task.Delay(delay, cancellationToken);

            response = await SendAuthenticatedAsync(HttpMethod.Get, pollUrl.ToString(), token, content: null, cancellationToken);
        }
    }

    private async Task<byte[]> DownloadAsync(string sasUrl, CancellationToken cancellationToken)
    {
        // The SAS URL carries its own credential; it must not be sent the bearer token.
        using var request = new HttpRequestMessage(HttpMethod.Get, sasUrl);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "downloading the invoice file", cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static byte[] ExtractPdf(byte[] payload)
    {
        if (payload is not [ZipMagic0, ZipMagic1, ..])
            return payload;

        using var stream = new MemoryStream(payload);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var pdfEntry = archive.Entries.SingleOrDefault(e =>
            e.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The downloaded ZIP did not contain exactly one PDF entry.");

        using var entryStream = pdfEntry.Open();
        using var buffer = new MemoryStream();
        entryStream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private Task<HttpResponseMessage> SendAuthenticatedAsync(
        HttpMethod method,
        string url,
        string token,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Content = content,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return httpClient.SendAsync(request, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"Microsoft 365 billing request failed while {action}: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
    }
}
