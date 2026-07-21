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

    [Fact]
    public async Task ListDrivesAsync_PagesAndFallsBackToOneDriveNameWhenBlank()
    {
        var handler = new StubHttpMessageHandler((request, index) => Json(index == 0
            ? """{"value":[{"id":"drive-1","name":"Company Files"},{"id":"drive-2","name":""}],"@odata.nextLink":"https://next.test/drives"}"""
            : """{"value":[{"id":"drive-3","name":"Personal"}]}"""));
        var discovery = Build(handler);

        var results = await discovery.ListDrivesAsync();

        Assert.Equal(3, results.Count);
        Assert.Contains(results, x => x.Id == "drive-1" && x.Name == "Company Files");
        Assert.Contains(results, x => x.Id == "drive-2" && x.Name == "OneDrive");
        Assert.Contains(results, x => x.Id == "drive-3" && x.Name == "Personal");
        Assert.All(handler.Requests, request => Assert.Equal("Bearer fake-access-token", request.Authorization));
    }

    [Fact]
    public async Task ListFolderChildrenAsync_RequestsDriveRoot_WhenFolderItemIdIsNull()
    {
        var handler = new StubHttpMessageHandler((request, _) => Json(
            """{"value":[{"id":"f1","name":"Bills","folder":{}},{"id":"file1","name":"notes.txt"}]}"""));
        var discovery = Build(handler);

        var results = await discovery.ListFolderChildrenAsync("drive-1", null);

        var single = Assert.Single(results);
        Assert.Equal("f1", single.Id);
        Assert.Equal("Bills", single.Name);
        Assert.Contains("/drives/drive-1/root/children", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task ListFolderChildrenAsync_RequestsItemChildren_AndFiltersOutNonFolders()
    {
        var handler = new StubHttpMessageHandler((request, _) => Json(
            """{"value":[{"id":"f2","name":"Microsoft 365","folder":{}},{"id":"file2","name":"invoice.pdf"}]}"""));
        var discovery = Build(handler);

        var results = await discovery.ListFolderChildrenAsync("drive-1", "folder-1");

        var single = Assert.Single(results);
        Assert.Equal("f2", single.Id);
        Assert.Equal("Microsoft 365", single.Name);
        Assert.Contains("/drives/drive-1/items/folder-1/children", handler.Requests[0].RequestUri!.ToString());
    }

    private static MicrosoftResourceDiscovery Build(HttpMessageHandler handler) =>
        new(new HttpClient(handler), new FakeMicrosoftTokenProvider());

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}
