using System.Net;
using InvoiceManager.Core.Integrations;
using InvoiceManager.Infrastructure.OneDrive;
using InvoiceManager.TestSupport;

namespace InvoiceManager.Infrastructure.Tests;

public sealed class GraphOneDriveIntegrationTests
{
    [Fact]
    public async Task UploadAsync_PutsPdfToGraphContentEndpoint_AndReturnsWebUrl()
    {
        var handler = new StubHttpMessageHandler((_, _) => Json(
            HttpStatusCode.Created,
            """{ "id": "01ABCDEF", "webUrl": "https://contoso-my.sharepoint.com/invoice.pdf" }"""));
        using var httpClient = new HttpClient(handler);
        var integration = new GraphOneDriveIntegration(httpClient, new FakeMicrosoftTokenProvider());

        var pdf = new byte[] { 1, 2, 3 };
        var result = await integration.UploadAsync(new OneDriveUploadRequest(
            "/drives/drive-1/root:/Bills/Microsoft 365",
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
        HttpRequestMessage? captured = null;
        var handler = new CapturingHandler(
            message => captured = message,
            Json(HttpStatusCode.OK, """{ "id": "1", "webUrl": "https://example/x.pdf" }"""));
        using var httpClient = new HttpClient(handler);
        var tokenProvider = new FakeMicrosoftTokenProvider("graph-token");
        var integration = new GraphOneDriveIntegration(httpClient, tokenProvider);

        await integration.UploadAsync(new OneDriveUploadRequest("/drives/d/root:/Bills", "x.pdf", [9]));

        Assert.Equal("Bearer", captured!.Headers.Authorization!.Scheme);
        Assert.Equal("graph-token", captured.Headers.Authorization.Parameter);
        var scopes = Assert.Single(tokenProvider.RequestedScopes);
        Assert.Contains("https://graph.microsoft.com/Files.ReadWrite.All", scopes);
    }

    [Fact]
    public async Task UploadAsync_Throws_WhenGraphReturnsError()
    {
        var handler = new StubHttpMessageHandler((_, _) => Json(HttpStatusCode.Forbidden, "denied"));
        using var httpClient = new HttpClient(handler);
        var integration = new GraphOneDriveIntegration(httpClient, new FakeMicrosoftTokenProvider());

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            integration.UploadAsync(new OneDriveUploadRequest("/drives/d/root:/Bills", "x.pdf", [1])));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    /// <summary>A handler that captures the request message (headers included) before responding.</summary>
    private sealed class CapturingHandler(Action<HttpRequestMessage> capture, HttpResponseMessage response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            capture(request);
            return Task.FromResult(response);
        }
    }
}
