using System.IO.Compression;
using System.Net;
using System.Text;
using InvoiceManager.Core;
using InvoiceManager.Core.Integrations;
using InvoiceManager.Integrations.Microsoft365;
using InvoiceManager.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NodaMoney;

namespace InvoiceManager.Integrations.Microsoft365.Tests;

public sealed class MicrosoftBillingInvoiceSourceTests
{
    private const string PollUrl = "https://management.azure.com/operationResults/op-1?api-version=2024-04-01";
    private const string NextPageUrl = "https://management.azure.com/providers/Microsoft.Billing/invoices-page-2";
    private const string SasUrl = "https://billing.blob.core.windows.net/downloads/G152207778.zip?sig=abc";

    private static readonly byte[] PdfBytes = "%PDF-1.7 fake invoice"u8.ToArray();

    private static InvoiceSearchCriteria Criteria(decimal amountTolerance = 0m) => new(
        BillingAccountId: "account:2019-05-31",
        ExpectedDate: new DateOnly(2025, 7, 10),
        DateToleranceDays: 5,
        AmountMatchingCriteria: new AmountMatchingCriteria(new Money(11.59m, "GBP"), amountTolerance));

    [Fact]
    public async Task FindInvoiceAsync_ReturnsMatchWithExtractedPdf_ForZippedDownload()
    {
        var handler = new StubHttpMessageHandler(Script(ZipContaining("G152207778.pdf", PdfBytes)));
        var source = BuildSource(handler);

        var result = await source.FindInvoiceAsync(Criteria());

        if (result is not InvoiceMatch match)
        {
            Assert.Fail($"Expected InvoiceMatch but got {result}.");
            return;
        }

        Assert.Equal(PdfBytes, match.PdfContent);
        Assert.Equal(new DateOnly(2025, 7, 12), match.Details.ActualInvoiceDate);
        Assert.Equal(new Money(11.59m, "GBP"), match.Details.ActualAmount);
        Assert.Equal(new SourceInvoiceId("G152207778"), match.Details.SourceInvoiceId);
    }

    [Fact]
    public async Task FindInvoiceAsync_ReturnsPdfDirectly_WhenDownloadIsNotZipped()
    {
        var handler = new StubHttpMessageHandler(Script(PdfBytes));
        var source = BuildSource(handler);

        var result = await source.FindInvoiceAsync(Criteria());

        if (result is not InvoiceMatch match)
        {
            Assert.Fail($"Expected InvoiceMatch but got {result}.");
            return;
        }

        Assert.Equal(PdfBytes, match.PdfContent);
    }

    [Fact]
    public async Task FindInvoiceAsync_PollsLocationUntilOk_ForAsyncDownload()
    {
        // downloadDocuments returns 202 twice before the 200 with the SAS url.
        var handler = new StubHttpMessageHandler(Script(PdfBytes, pollsBeforeReady: 2));
        var source = BuildSource(handler);

        var result = await source.FindInvoiceAsync(Criteria());

        Assert.True(result is InvoiceMatch, $"Expected InvoiceMatch but got {result}.");
        // list + downloadDocuments(202) + 2 polls(202,200)... plus sas download.
        Assert.Contains(handler.Requests, r => r.RequestUri!.ToString() == PollUrl);
        Assert.True(handler.Requests.Count(r => r.RequestUri!.ToString() == PollUrl) >= 2);
    }

    [Fact]
    public async Task FindInvoiceAsync_ReturnsNoMatch_WhenAmountOutsideTolerance()
    {
        var handler = new StubHttpMessageHandler(Script(PdfBytes, invoiceAmount: 99.99m));
        var source = BuildSource(handler);

        var result = await source.FindInvoiceAsync(Criteria());

        Assert.True(result is NoInvoiceMatch, $"Expected NoInvoiceMatch but got {result}.");
        // Only the list call should have been made — no download attempted.
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task FindInvoiceAsync_MatchesByDateOnly_WhenAmountCriteriaAreAbsent()
    {
        var handler = new StubHttpMessageHandler(Script(PdfBytes, invoiceAmount: 999.99m));
        var source = BuildSource(handler, IntegrationType.Azure);

        var result = await source.FindInvoiceAsync(CriteriaWithoutAmount());

        Assert.True(result is InvoiceMatch, $"Expected InvoiceMatch but got {result}.");
    }

    [Fact]
    public async Task FindInvoiceAsync_MatchesWithinAmountTolerance()
    {
        var handler = new StubHttpMessageHandler(Script(PdfBytes, invoiceAmount: 11.50m));
        var source = BuildSource(handler);

        var result = await source.FindInvoiceAsync(Criteria(amountTolerance: 0.10m));

        Assert.True(result is InvoiceMatch, $"Expected InvoiceMatch but got {result}.");
    }

    [Fact]
    public async Task FindInvoiceAsync_AuthenticatesBillingCalls_ButNotSasDownload()
    {
        var authByUri = new Dictionary<string, bool>();
        var handler = new StubHttpMessageHandler((request, index) =>
        {
            authByUri[request.RequestUri!.ToString()] = request.Headers.Authorization is not null;
            return Script(PdfBytes)(request, index);
        });
        var source = BuildSource(handler);

        await source.FindInvoiceAsync(Criteria());

        Assert.True(authByUri.Single(kvp => kvp.Key.Contains("/invoices")).Value, "billing list call should be authenticated");
        Assert.False(authByUri[SasUrl], "SAS download must not carry the bearer token");
    }

    [Fact]
    public async Task FindInvoiceAsync_RequestsFourteenMonthsOfBillingPeriods()
    {
        var handler = new StubHttpMessageHandler(Script(PdfBytes));
        var source = BuildSource(handler);

        await source.FindInvoiceAsync(Criteria());

        var listRequest = handler.Requests.Single(r => r.RequestUri!.AbsoluteUri.Contains("/invoices"));
        Assert.Contains("periodStartDate=2024-05-10", listRequest.RequestUri!.Query);
        Assert.Contains("periodEndDate=2025-07-15", listRequest.RequestUri!.Query);
    }

    [Fact]
    public async Task FindInvoiceAsync_FollowsInvoiceListPagination()
    {
        var handler = new StubHttpMessageHandler(Script(PdfBytes, paginateInvoiceList: true));
        var source = BuildSource(handler);

        var result = await source.FindInvoiceAsync(Criteria());

        Assert.True(result is InvoiceMatch, $"Expected InvoiceMatch but got {result}.");
        Assert.Contains(handler.Requests, request => request.RequestUri!.ToString() == NextPageUrl);
    }

    private static MicrosoftBillingInvoiceSource BuildSource(
        StubHttpMessageHandler handler, IntegrationType integrationType = IntegrationType.Microsoft365)
    {
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new MicrosoftBillingOptions { PollInterval = TimeSpan.Zero });
        return new MicrosoftBillingInvoiceSource(
            httpClient,
            new FakeMicrosoftTokenProvider(),
            options,
            NullLogger<MicrosoftBillingInvoiceSource>.Instance,
            integrationType);
    }

    private static InvoiceSearchCriteria CriteriaWithoutAmount() => new(
        BillingAccountId: "azure-account",
        ExpectedDate: new DateOnly(2025, 7, 10),
        DateToleranceDays: 5,
        AmountMatchingCriteria: Option.None);

    /// <summary>
    /// Builds a responder that walks the whole flow: list invoices, downloadDocuments
    /// (202 with Location), zero or more polls (202), then 200 with the SAS url, then
    /// the SAS download of <paramref name="downloadBytes"/>.
    /// </summary>
    private static Func<HttpRequestMessage, int, HttpResponseMessage> Script(
        byte[] downloadBytes,
        int pollsBeforeReady = 0,
        decimal invoiceAmount = 11.59m,
        bool paginateInvoiceList = false)
    {
        var polls = 0;
        var invoiceListRequests = 0;
        return (request, _) =>
        {
            var uri = request.RequestUri!.ToString();

            if (uri == SasUrl)
                return Bytes(downloadBytes);

            if (uri == PollUrl)
                return polls++ < pollsBeforeReady ? Accepted() : Json(HttpStatusCode.OK, $$"""{ "url": "{{SasUrl}}" }""");

            if (uri.Contains("/downloadDocuments"))
                return Accepted();

            if (uri.Contains("/invoices") || uri == NextPageUrl)
            {
                if (paginateInvoiceList && invoiceListRequests++ == 0)
                {
                    return Json(
                        HttpStatusCode.OK,
                        $$"""{ "value": [], "nextLink": "{{NextPageUrl}}" }""");
                }

                return Json(HttpStatusCode.OK, InvoiceListJson(invoiceAmount));
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
    }

    private static string InvoiceListJson(decimal amount) => $$"""
        {
          "value": [
            {
              "name": "G152207778",
              "properties": {
                "invoiceDate": "2025-07-12T00:00:00Z",
                "totalAmount": { "currency": "GBP", "value": {{amount}} },
                "documents": [
                  { "name": "invoice.pdf", "kind": "Invoice" },
                  { "name": "creditnote.pdf", "kind": "CreditNote" }
                ]
              }
            }
          ]
        }
        """;

    private static HttpResponseMessage Accepted()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Accepted);
        response.Headers.Location = new Uri(PollUrl);
        return response;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Bytes(byte[] bytes) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private static byte[] ZipContaining(string entryName, byte[] content)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
            using var entryStream = entry.Open();
            entryStream.Write(content);
        }

        return buffer.ToArray();
    }
}
