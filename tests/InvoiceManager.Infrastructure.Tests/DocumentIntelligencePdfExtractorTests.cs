using System.Net;
using Azure.Core;
using InvoiceManager.Core.Integrations;
using InvoiceManager.Infrastructure.DocumentIntelligence;
using InvoiceManager.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NodaMoney;

namespace InvoiceManager.Infrastructure.Tests;

public sealed class DocumentIntelligencePdfExtractorTests
{
    private const string OperationUrl = "https://docintel.cognitiveservices.azure.com/documentintelligence/operations/op-1?api-version=2024-11-30";

    private static readonly byte[] PdfBytes = "%PDF-1.7 fake invoice"u8.ToArray();

    [Fact]
    public async Task ExtractAsync_ReturnsSucceeded_WhenFieldsMeetConfidenceThreshold()
    {
        var handler = new StubHttpMessageHandler(Script(AnalyzeResultJson(
            dateConfidence: 0.95, totalConfidence: 0.9, amount: 11.59m, currency: "GBP")));
        var extractor = Build(handler);

        var result = await extractor.ExtractAsync(PdfBytes);

        if (result is not PdfExtractionSucceeded succeeded)
        {
            Assert.Fail($"Expected PdfExtractionSucceeded but got {result}.");
            return;
        }

        Assert.Equal(new DateOnly(2026, 4, 10), succeeded.InvoiceDate);
        Assert.Equal(new Money(11.59m, "GBP"), succeeded.Total);
    }

    [Fact]
    public async Task ExtractAsync_PollsOperationLocation_UntilSucceeded()
    {
        var handler = new StubHttpMessageHandler(Script(
            AnalyzeResultJson(dateConfidence: 0.95, totalConfidence: 0.9, amount: 11.59m, currency: "GBP"),
            pollsBeforeReady: 2));
        var extractor = Build(handler, pollInterval: TimeSpan.Zero);

        var result = await extractor.ExtractAsync(PdfBytes);

        Assert.True(result is PdfExtractionSucceeded, $"Expected PdfExtractionSucceeded but got {result}.");
        Assert.True(handler.Requests.Count(r => r.RequestUri!.ToString() == OperationUrl) >= 2);
    }

    [Fact]
    public async Task ExtractAsync_ReturnsFailed_WhenInvoiceDateConfidenceBelowThreshold()
    {
        var handler = new StubHttpMessageHandler(Script(AnalyzeResultJson(
            dateConfidence: 0.4, totalConfidence: 0.9, amount: 11.59m, currency: "GBP")));
        var extractor = Build(handler);

        var result = await extractor.ExtractAsync(PdfBytes);

        Assert.True(result is PdfExtractionFailed, $"Expected PdfExtractionFailed but got {result}.");
    }

    [Fact]
    public async Task ExtractAsync_ReturnsFailed_WhenInvoiceTotalFieldMissing()
    {
        var handler = new StubHttpMessageHandler(Script($$"""
            {
              "status": "succeeded",
              "analyzeResult": {
                "documents": [
                  {
                    "fields": {
                      "InvoiceDate": { "valueDate": "2026-04-10", "confidence": 0.95 }
                    }
                  }
                ]
              }
            }
            """));
        var extractor = Build(handler);

        var result = await extractor.ExtractAsync(PdfBytes);

        Assert.True(result is PdfExtractionFailed, $"Expected PdfExtractionFailed but got {result}.");
    }

    [Fact]
    public async Task ExtractAsync_ReturnsFailed_WhenAnalysisReportsFailedStatus()
    {
        var handler = new StubHttpMessageHandler(Script("""
            { "status": "failed", "error": { "message": "Unsupported content." } }
            """));
        var extractor = Build(handler);

        var result = await extractor.ExtractAsync(PdfBytes);

        if (result is not PdfExtractionFailed failed)
        {
            Assert.Fail($"Expected PdfExtractionFailed but got {result}.");
            return;
        }

        Assert.Contains("Unsupported content.", failed.Reason);
    }

    [Fact]
    public async Task ExtractAsync_SendsBearerToken_ForCognitiveServicesScope()
    {
        var handler = new StubHttpMessageHandler(Script(
            AnalyzeResultJson(dateConfidence: 0.95, totalConfidence: 0.9, amount: 11.59m, currency: "GBP")));
        var credential = new FakeTokenCredential("cognitive-token");
        var extractor = Build(handler, credential);

        await extractor.ExtractAsync(PdfBytes);

        Assert.Contains(handler.Requests, r => r.Authorization == "Bearer cognitive-token");
        Assert.Contains("https://cognitiveservices.azure.com/.default", credential.RequestedScopes.Single());
    }

    private static DocumentIntelligencePdfExtractor Build(
        StubHttpMessageHandler handler, TokenCredential? credential = null, TimeSpan? pollInterval = null)
    {
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new DocumentIntelligenceOptions
        {
            Endpoint = new Uri("https://docintel.cognitiveservices.azure.com/"),
            PollInterval = pollInterval ?? TimeSpan.Zero,
        });
        return new DocumentIntelligencePdfExtractor(
            httpClient, credential ?? new FakeTokenCredential(), options,
            NullLogger<DocumentIntelligencePdfExtractor>.Instance);
    }

    private static string AnalyzeResultJson(double dateConfidence, double totalConfidence, decimal amount, string currency) => $$"""
        {
          "status": "succeeded",
          "analyzeResult": {
            "documents": [
              {
                "fields": {
                  "InvoiceDate": { "valueDate": "2026-04-10", "confidence": {{dateConfidence}} },
                  "InvoiceTotal": {
                    "valueCurrency": { "amount": {{amount}}, "currencyCode": "{{currency}}" },
                    "confidence": {{totalConfidence}}
                  }
                }
              }
            ]
          }
        }
        """;

    /// <summary>
    /// Builds a responder that walks the whole flow: analyze (202 with Operation-Location),
    /// zero or more polls (still running), then 200 with the final result JSON.
    /// </summary>
    private static Func<HttpRequestMessage, int, HttpResponseMessage> Script(
        string resultJson, int pollsBeforeReady = 0)
    {
        var polls = 0;
        return (request, _) =>
        {
            var uri = request.RequestUri!.ToString();

            if (uri == OperationUrl)
                return polls++ < pollsBeforeReady
                    ? Json(HttpStatusCode.OK, """{ "status": "running" }""")
                    : Json(HttpStatusCode.OK, resultJson);

            if (uri.Contains(":analyze"))
            {
                var response = new HttpResponseMessage(HttpStatusCode.Accepted);
                response.Headers.Location = new Uri(OperationUrl);
                return response;
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    /// <summary>A <see cref="TokenCredential"/> test double that returns a fixed token and records requested scopes.</summary>
    private sealed class FakeTokenCredential(string token = "fake-token") : TokenCredential
    {
        private readonly List<string[]> requestedScopes = [];

        public IReadOnlyList<string[]> RequestedScopes => requestedScopes;

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            requestedScopes.Add(requestContext.Scopes);
            return new AccessToken(token, DateTimeOffset.UtcNow.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            requestedScopes.Add(requestContext.Scopes);
            return new ValueTask<AccessToken>(new AccessToken(token, DateTimeOffset.UtcNow.AddHours(1)));
        }
    }
}
