using System.Net;
using System.Text;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using InvoiceManager.TestSupport;

namespace InvoiceManager.Infrastructure.Tests;

public sealed class MicrosoftResourceDiscoveryTests
{
    [Fact]
    public async Task BillingDiscovery_PagesAndListsAllAccountTypes()
    {
        var handler = new StubHttpMessageHandler((request, index) => Json(index == 0
            ? """{"value":[{"name":"business-id","properties":{"accountType":"Business","displayName":"M365"}}],"nextLink":"https://next.test/accounts"}"""
            : """{"value":[{"name":"individual-id","properties":{"accountType":"Individual","displayName":"Azure"}}]}"""));
        var discovery = Build(handler);

        var results = await discovery.ListBillingAccountsAsync();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, x => x.Id == "business-id" && x.AccountType == "Business");
        Assert.Contains(results, x => x.Id == "individual-id" && x.AccountType == "Individual");
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal("Bearer fake-access-token", request.Authorization));
    }

    private static MicrosoftResourceDiscovery Build(HttpMessageHandler handler) =>
        new(new HttpClient(handler), new FakeMicrosoftTokenProvider());

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}
