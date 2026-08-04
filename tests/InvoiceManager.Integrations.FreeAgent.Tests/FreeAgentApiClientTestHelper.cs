using InvoiceManager.Infrastructure.FreeAgentAuthorization;
using InvoiceManager.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InvoiceManager.Integrations.FreeAgent.Tests;

internal sealed class FakeFreeAgentTokenProvider : IFreeAgentTokenProvider
{
    public string Token { get; set; } = "test-access-token";

    public Task<string> AcquireTokenAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Token);
}

internal static class TestClientFactory
{
    public static FreeAgentApiClient Create(StubHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new FreeAgentOptions { Environment = FreeAgentEnvironment.Sandbox });
        return new FreeAgentApiClient(
            httpClient, new FakeFreeAgentTokenProvider(), options, NullLogger<FreeAgentApiClient>.Instance);
    }
}
