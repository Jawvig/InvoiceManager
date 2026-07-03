namespace InvoiceManager.TestSupport;

/// <summary>
/// An <see cref="HttpMessageHandler"/> driven by a caller-supplied responder,
/// recording every request it handled (including the read request body) so tests
/// can assert on them. The responder receives the zero-based request index so it
/// can script a sequence of responses (for example a 202 followed by polls).
/// </summary>
public sealed class StubHttpMessageHandler(
    Func<HttpRequestMessage, int, HttpResponseMessage> responder) : HttpMessageHandler
{
    private readonly List<RecordedRequest> requests = [];

    public IReadOnlyList<RecordedRequest> Requests => requests;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        var index = requests.Count;
        requests.Add(new RecordedRequest(request.Method, request.RequestUri, body));
        return responder(request, index);
    }
}

/// <summary>A captured request: method, URI, and body text if any.</summary>
public sealed record RecordedRequest(HttpMethod Method, Uri? RequestUri, string? Body);
