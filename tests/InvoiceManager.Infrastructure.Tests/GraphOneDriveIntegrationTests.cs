using System.Globalization;
using System.Net;
using InvoiceManager.Core;
using InvoiceManager.Core.Integrations;
using InvoiceManager.Infrastructure.OneDrive;
using InvoiceManager.TestSupport;
using NodaMoney;

namespace InvoiceManager.Infrastructure.Tests;

public sealed class GraphOneDriveIntegrationTests
{
    private const string Folder = "/drives/drive-1/root:/Bills/Microsoft 365";

    private static readonly InvoiceFilename Filename = new(
        new InvoiceFilenameSettings { Culture = CultureInfo.GetCultureInfo("en-GB") });

    [Fact]
    public async Task UploadAsync_PutsPdfToGraphContentEndpoint_AndReturnsWebUrl()
    {
        var handler = new StubHttpMessageHandler((_, _) => Json(
            HttpStatusCode.Created,
            """{ "id": "01ABCDEF", "webUrl": "https://contoso-my.sharepoint.com/invoice.pdf" }"""));
        using var httpClient = new HttpClient(handler);
        var integration = Build(httpClient);

        var pdf = new byte[] { 1, 2, 3 };
        var result = await integration.UploadAsync(new OneDriveUploadRequest(
            Folder,
            "2025-07-12 Microsoft 365 Business Basic G152207778 £11.59 exc.pdf",
            pdf));

        Assert.Equal("https://contoso-my.sharepoint.com/invoice.pdf", result.OneDriveLocation);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.StartsWith(
            "https://graph.microsoft.com/v1.0/drives/drive-1/root:/Bills/Microsoft 365/",
            Uri.UnescapeDataString(request.RequestUri!.ToString()));
        Assert.EndsWith(":/content", request.RequestUri!.ToString());
    }

    [Fact]
    public async Task UploadAsync_SendsBearerToken_ForGraphFilesScope()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Json(HttpStatusCode.OK, """{ "id": "1", "webUrl": "https://example/x.pdf" }"""));
        using var httpClient = new HttpClient(handler);
        var tokenProvider = new FakeMicrosoftTokenProvider("graph-token");
        var integration = Build(httpClient, tokenProvider);

        await integration.UploadAsync(new OneDriveUploadRequest("/drives/d/root:/Bills", "x.pdf", [9]));

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer graph-token", request.Authorization);
        var scopes = Assert.Single(tokenProvider.RequestedScopes);
        Assert.Contains("https://graph.microsoft.com/Files.ReadWrite.All", scopes);
    }

    [Fact]
    public async Task UploadAsync_Throws_WhenGraphReturnsError()
    {
        var handler = new StubHttpMessageHandler((_, _) => Json(HttpStatusCode.Forbidden, "denied"));
        using var httpClient = new HttpClient(handler);
        var integration = Build(httpClient);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            integration.UploadAsync(new OneDriveUploadRequest("/drives/d/root:/Bills", "x.pdf", [1])));
    }

    [Fact]
    public async Task SearchAsync_ReturnsMatch_WithParsedDetailsAndReason_WhenAFileSatisfiesCriteria()
    {
        var handler = new StubHttpMessageHandler((_, _) => Json(HttpStatusCode.OK, Children(
            nextLink: null,
            ("2026-07-10 Microsoft 365 Business Basic G152207778 £11.59 exc.pdf", "id-1", "https://example/id-1"))));
        using var httpClient = new HttpClient(handler);
        var integration = Build(httpClient);

        var result = await integration.SearchAsync(new OneDriveSearchRequest(Folder, Criteria()));

        var match = AssertMatch(result);
        Assert.Equal("https://example/id-1", match.OneDriveDetails.OneDriveLocation);
        Assert.Equal(new DateOnly(2026, 7, 10), match.Details.ActualInvoiceDate);
        Assert.Equal(new Money(11.59m, "GBP"), match.Details.ActualAmount);
        Assert.Equal("G152207778", match.Details.SourceInvoiceId.Value);
        Assert.Contains("2026-07-10", match.MatchReason);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.EndsWith(":/children", request.RequestUri!.ToString());
    }

    [Theory]
    [InlineData("2026-07-13", true)]   // three days late: on the tolerance boundary.
    [InlineData("2026-07-14", false)]  // four days late: just outside the window.
    public async Task SearchAsync_HonoursDateTolerance(string fileDate, bool expectMatch)
    {
        var handler = new StubHttpMessageHandler((_, _) => Json(HttpStatusCode.OK, Children(
            nextLink: null,
            ($"{fileDate} Microsoft 365 Business Basic G152207778 £11.59 exc.pdf", "id-1", "https://example/id-1"))));
        using var httpClient = new HttpClient(handler);
        var integration = Build(httpClient);

        var result = await integration.SearchAsync(new OneDriveSearchRequest(Folder, Criteria()));

        Assert.Equal(expectMatch, result is OneDriveMatch);
    }

    [Fact]
    public async Task SearchAsync_ReturnsNoMatch_WhenCurrencyDiffers()
    {
        var handler = new StubHttpMessageHandler((_, _) => Json(HttpStatusCode.OK, Children(
            nextLink: null,
            ("2026-07-10 Microsoft 365 Business Basic G152207778 €11.59 exc.pdf", "id-1", "https://example/id-1"))));
        using var httpClient = new HttpClient(handler);
        var integration = Build(httpClient);

        var result = await integration.SearchAsync(new OneDriveSearchRequest(Folder, Criteria()));

        Assert.True(result is NoOneDriveMatch, $"Expected NoOneDriveMatch but got {result}.");
    }

    [Fact]
    public async Task SearchAsync_IgnoresNearMissAndUnrelatedFiles_AndReturnsTheValidMatch()
    {
        var handler = new StubHttpMessageHandler((_, _) => Json(HttpStatusCode.OK, Children(
            nextLink: null,
            ("report.pdf", "id-0", "https://example/id-0"),
            ("2026-7-10 Microsoft 365 Business Basic G1 £11.59 exc.pdf", "id-1", "https://example/id-1"),
            ("2026-07-10 Microsoft 365 Business Basic G152207778 £11.59 exc.pdf", "id-2", "https://example/id-2"))));
        using var httpClient = new HttpClient(handler);
        var integration = Build(httpClient);

        var result = await integration.SearchAsync(new OneDriveSearchRequest(Folder, Criteria()));

        var match = AssertMatch(result);
        Assert.Equal("https://example/id-2", match.OneDriveDetails.OneDriveLocation);
    }

    [Fact]
    public async Task SearchAsync_PicksClosestByDate_WhenSeveralCandidatesMatch()
    {
        var handler = new StubHttpMessageHandler((_, _) => Json(HttpStatusCode.OK, Children(
            nextLink: null,
            ("2026-07-08 Microsoft 365 Business Basic G1 £11.59 exc.pdf", "id-far", "https://example/far"),
            ("2026-07-11 Microsoft 365 Business Basic G2 £11.59 exc.pdf", "id-near", "https://example/near"))));
        using var httpClient = new HttpClient(handler);
        var integration = Build(httpClient);

        var result = await integration.SearchAsync(new OneDriveSearchRequest(Folder, Criteria()));

        var match = AssertMatch(result);
        Assert.Equal("https://example/near", match.OneDriveDetails.OneDriveLocation);
    }

    [Fact]
    public async Task SearchAsync_FollowsPaging_AcrossNextLink()
    {
        var page2Url = "https://graph.microsoft.com/v1.0/drives/drive-1/root/children?$skiptoken=abc";
        var handler = new StubHttpMessageHandler((_, index) => index switch
        {
            0 => Json(HttpStatusCode.OK, Children(
                nextLink: page2Url,
                ("report.pdf", "id-0", "https://example/id-0"))),
            _ => Json(HttpStatusCode.OK, Children(
                nextLink: null,
                ("2026-07-10 Microsoft 365 Business Basic G152207778 £11.59 exc.pdf", "id-1", "https://example/id-1"))),
        });
        using var httpClient = new HttpClient(handler);
        var integration = Build(httpClient);

        var result = await integration.SearchAsync(new OneDriveSearchRequest(Folder, Criteria()));

        var match = AssertMatch(result);
        Assert.Equal("https://example/id-1", match.OneDriveDetails.OneDriveLocation);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(page2Url, handler.Requests[1].RequestUri!.ToString());
    }

    [Fact]
    public async Task SearchAsync_ReturnsMatch_WhenFileOmitsTheVatIndicator()
    {
        // A manually-saved file may lack the trailing "inc"/"exc" indicator. Matching is on
        // date, amount, and description, so the file still reconciles.
        var handler = new StubHttpMessageHandler((_, _) => Json(HttpStatusCode.OK, Children(
            nextLink: null,
            ("2026-07-10 Microsoft 365 Business Basic G152207778 £11.59.pdf", "id-1", "https://example/id-1"))));
        using var httpClient = new HttpClient(handler);
        var integration = Build(httpClient);

        var result = await integration.SearchAsync(new OneDriveSearchRequest(Folder, Criteria()));

        var match = AssertMatch(result);
        Assert.Equal("https://example/id-1", match.OneDriveDetails.OneDriveLocation);
        Assert.Equal(new Money(11.59m, "GBP"), match.Details.ActualAmount);
    }

    [Fact]
    public async Task SearchAsync_MatchesDescriptionFreeFileByDateOnly_WhenAmountCriteriaAreAbsent()
    {
        var handler = new StubHttpMessageHandler((_, _) => Json(HttpStatusCode.OK, Children(
            nextLink: null,
            ("2026-07-10 G152207778 £999.99.pdf", "id-1", "https://example/id-1"))));
        using var httpClient = new HttpClient(handler);
        var integration = Build(httpClient);

        var criteria = new OneDriveSearchCriteria(
            new DateOnly(2026, 7, 10), 3, Option.None, "");
        var result = await integration.SearchAsync(new OneDriveSearchRequest(Folder, criteria));

        var match = AssertMatch(result);
        Assert.Equal("G152207778", match.Details.SourceInvoiceId.Value);
    }

    [Fact]
    public async Task SearchAsync_ReturnsNoMatch_WhenDescriptionDiffers()
    {
        // Same date, amount, and currency, but a different subscription's file sharing
        // the folder: the description must match, so this is not reconciled.
        var handler = new StubHttpMessageHandler((_, _) => Json(HttpStatusCode.OK, Children(
            nextLink: null,
            ("2026-07-10 Microsoft 365 Copilot G152207778 £11.59 exc.pdf", "id-1", "https://example/id-1"))));
        using var httpClient = new HttpClient(handler);
        var integration = Build(httpClient);

        var result = await integration.SearchAsync(new OneDriveSearchRequest(Folder, Criteria()));

        Assert.True(result is NoOneDriveMatch, $"Expected NoOneDriveMatch but got {result}.");
    }

    private static OneDriveMatch AssertMatch(OneDriveSearchResult result) =>
        result is OneDriveMatch match
            ? match
            : throw new Xunit.Sdk.XunitException($"Expected OneDriveMatch but got {result}.");

    private static GraphOneDriveIntegration Build(
        HttpClient httpClient,
        FakeMicrosoftTokenProvider? tokenProvider = null) =>
        new(httpClient, tokenProvider ?? new FakeMicrosoftTokenProvider(), Filename);

    private static OneDriveSearchCriteria Criteria() => new(
        ExpectedDate: new DateOnly(2026, 7, 10),
        DateToleranceDays: 3,
        AmountMatchingCriteria: new AmountMatchingCriteria(new Money(11.59m, "GBP"), 0m),
        InvoiceDescription: "Microsoft 365 Business Basic");

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static string Children(string? nextLink, params (string Name, string Id, string WebUrl)[] items)
    {
        var values = string.Join(",", items.Select(i =>
            $$"""{ "id": "{{i.Id}}", "name": {{System.Text.Json.JsonSerializer.Serialize(i.Name)}}, "webUrl": "{{i.WebUrl}}" }"""));
        var next = nextLink is null ? "" : $""", "@odata.nextLink": "{nextLink}" """;
        return $$"""{ "value": [{{values}}]{{next}} }""";
    }
}
