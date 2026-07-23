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

    [Fact]
    public async Task GetFolderAsync_ReturnsResolvedFolder_WithCanonicalLeadingSlashPath()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
            request.RequestUri!.ToString().Contains("/items/")
                ? Json("""{"id":"folder-1","name":"Microsoft 365","folder":{},"parentReference":{"path":"/drives/drive-1/root:/Bills"}}""")
                : Json("""{"name":"Company OneDrive"}"""));
        var discovery = Build(handler);

        var result = await discovery.GetFolderAsync("drive-1", "folder-1");

        Assert.NotNull(result);
        Assert.Equal("drive-1", result!.DriveId);
        Assert.Equal("Company OneDrive", result.DriveName);
        Assert.Equal("folder-1", result.FolderItemId);
        Assert.Equal("/Bills/Microsoft 365", result.FolderPath);
    }

    [Fact]
    public async Task GetFolderAsync_ReturnsRootLevelPath_WithLeadingSlash_WhenFolderIsAtDriveRoot()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
            request.RequestUri!.ToString().Contains("/items/")
                ? Json("""{"id":"folder-1","name":"Bills","folder":{},"parentReference":{"path":"/drives/drive-1/root:"}}""")
                : Json("""{"name":"OneDrive"}"""));
        var discovery = Build(handler);

        var result = await discovery.GetFolderAsync("drive-1", "folder-1");

        Assert.NotNull(result);
        Assert.Equal("/Bills", result!.FolderPath);
    }

    [Fact]
    public async Task GetFolderAsync_FallsBackToOneDriveName_WhenDriveNameIsBlank()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
            request.RequestUri!.ToString().Contains("/items/")
                ? Json("""{"id":"folder-1","name":"Bills","folder":{},"parentReference":{"path":"/drives/drive-1/root:"}}""")
                : Json("""{"name":""}"""));
        var discovery = Build(handler);

        var result = await discovery.GetFolderAsync("drive-1", "folder-1");

        Assert.Equal("OneDrive", result!.DriveName);
    }

    [Fact]
    public async Task GetFolderAsync_ReturnsNull_WhenItemDoesNotExist()
    {
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));
        var discovery = Build(handler);

        var result = await discovery.GetFolderAsync("drive-1", "missing-item");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetFolderAsync_ReturnsNull_WhenItemIdIsMalformed()
    {
        // A forged/malformed drive or item ID commonly comes back as 400 from Graph (rejected
        // before it can even be looked up), not 404 — this must not surface as an unhandled
        // exception (which would 500 the Create/Edit page) but as an invalid selection, same as
        // a genuine 404.
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var discovery = Build(handler);

        var result = await discovery.GetFolderAsync("drive-1", "not-a-real-id");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetFolderAsync_Throws_WhenGraphReturnsAGenuineServerError()
    {
        // Unlike 400/404, a 5xx (or 401/403) is not the caller's fault to fix by picking a
        // different folder, so it must still surface as a failure rather than a silent null.
        var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var discovery = Build(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => discovery.GetFolderAsync("drive-1", "folder-1"));
    }

    [Fact]
    public async Task GetFolderAsync_ReturnsNull_WhenItemIsNotAFolder()
    {
        // A file (no "folder" facet) is a real Graph item but not a valid selection.
        var handler = new StubHttpMessageHandler((_, _) => Json(
            """{"id":"file-1","name":"invoice.pdf","parentReference":{"path":"/drives/drive-1/root:/Bills"}}"""));
        var discovery = Build(handler);

        var result = await discovery.GetFolderAsync("drive-1", "file-1");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetFolderAsync_ReturnsNull_WhenDriveNoLongerExists()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
            request.RequestUri!.ToString().Contains("/items/")
                ? Json("""{"id":"folder-1","name":"Bills","folder":{},"parentReference":{"path":"/drives/drive-1/root:"}}""")
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        var discovery = Build(handler);

        var result = await discovery.GetFolderAsync("drive-1", "folder-1");

        Assert.Null(result);
    }

    private static MicrosoftResourceDiscovery Build(HttpMessageHandler handler) =>
        new(new HttpClient(handler), new FakeMicrosoftTokenProvider());

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}
