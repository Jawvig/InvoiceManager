using System.Net;
using System.Text;
using InvoiceManager.Core;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using InvoiceManager.TestSupport;

namespace InvoiceManager.Infrastructure.Tests;

public sealed class MicrosoftResourceDiscoveryTests
{
    [Theory]
    [InlineData(IntegrationType.Microsoft365, "business-id")]
    [InlineData(IntegrationType.Azure, "individual-id")]
    public async Task BillingDiscovery_PagesAndFiltersByIntegrationType(
        IntegrationType integrationType,
        string expectedId)
    {
        var handler = new StubHttpMessageHandler((request, index) => Json(index == 0
            ? """{"value":[{"name":"business-id","properties":{"accountType":"Business","displayName":"M365"}}],"nextLink":"https://next.test/accounts"}"""
            : """{"value":[{"name":"individual-id","properties":{"accountType":"Individual","displayName":"Azure"}}]}"""));
        var discovery = Build(handler);

        var results = await discovery.ListBillingAccountsAsync(integrationType);

        Assert.Equal(expectedId, Assert.Single(results).Id);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal("Bearer fake-access-token", request.Authorization));
    }

    [Fact]
    public async Task FolderDiscovery_BrowsesDefaultDriveAndPagesExistingFolders()
    {
        var handler = new StubHttpMessageHandler((request, index) => Json(index switch
        {
            0 => """{"id":"drive-1"}""",
            1 => """{"value":[{"id":"folder-a","name":"Bills","folder":{}}],"@odata.nextLink":"https://next.test/root"}""",
            2 => """{"value":[{"id":"folder-b","name":"Archive","folder":{}}]}""",
            3 => """{"value":[{"id":"folder-c","name":"Microsoft","folder":{}}]}""",
            _ => """{"value":[]}""",
        }));
        var discovery = Build(handler);

        var results = await discovery.ListOneDriveFoldersAsync();

        Assert.Contains(results, x => x.Destination == new OneDriveDestination("/Bills", "drive-1", "folder-a"));
        Assert.Contains(results, x => x.Destination.DisplayPath == "/Bills/Microsoft");
        Assert.All(results, x => Assert.True(x.Destination.HasStableIds));
    }

    [Fact]
    public async Task ResolveLegacyPath_ReturnsStableDriveAndItemIds()
    {
        var discovery = Build(new StubHttpMessageHandler((_, _) =>
            Json("""{"id":"folder-42","parentReference":{"driveId":"drive-9"}}""")));

        var result = await discovery.ResolveLegacyOneDrivePathAsync("/drives/drive-9/root:/Bills");

        var destination = result switch
        {
            OneDriveDestination value => value,
            _ => throw new Xunit.Sdk.XunitException("Expected a resolved OneDrive destination."),
        };
        Assert.Equal("drive-9", destination.DriveId);
        Assert.Equal("folder-42", destination.FolderItemId);
    }

    private static MicrosoftResourceDiscovery Build(HttpMessageHandler handler) =>
        new(new HttpClient(handler), new FakeMicrosoftTokenProvider());

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}
