using InvoiceManager.Core.Integrations.FreeAgent;
using InvoiceManager.TestSupport;

namespace InvoiceManager.Integrations.FreeAgent.Tests;

public sealed class FreeAgentContactDirectoryTests
{
    private const string ContactUrl = "https://api.sandbox.freeagent.com/v2/contacts/1";

    [Fact]
    public async Task SearchAsync_MatchesOrganisationOrPersonName_CaseInsensitively()
    {
        var handler = new StubHttpMessageHandler((request, index) =>
            index switch
            {
                0 => JsonResponse(ContactsPageJson(
                    ("Acme Widgets Ltd", null, null),
                    (null, "Jane", "Smith"),
                    ("Other Co", null, null))),
                _ => JsonResponse(EmptyPageJson()),
            });
        var client = TestClientFactory.Create(handler);
        var directory = new FreeAgentContactDirectory(client);

        var results = await directory.SearchAsync("acme");

        var contact = Assert.Single(results);
        Assert.Equal("Acme Widgets Ltd", contact.DisplayName);
    }

    [Fact]
    public async Task SearchAsync_StopsPaging_OnceMaxResultsFound()
    {
        var pageCalls = 0;
        var handler = new StubHttpMessageHandler((request, index) =>
        {
            pageCalls++;
            var contacts = Enumerable.Range(0, 100).Select(_ => ((string?)"Acme", (string?)null, (string?)null));
            return JsonResponse(ContactsPageJson(contacts.ToArray()));
        });
        var client = TestClientFactory.Create(handler);
        var directory = new FreeAgentContactDirectory(client);

        var results = await directory.SearchAsync("acme");

        Assert.Equal(20, results.Count);
        Assert.Equal(1, pageCalls);
    }

    [Fact]
    public async Task SearchAsync_ScansAtMostFivePages_WhenFewMatchesFound()
    {
        var pageCalls = 0;
        var handler = new StubHttpMessageHandler((request, index) =>
        {
            pageCalls++;
            // Every page is full (100 non-matching contacts) so paging never naturally stops -
            // the page cap is the only thing that bounds this.
            var contacts = Enumerable.Range(0, 100).Select(_ => ((string?)"No Match", (string?)null, (string?)null));
            return JsonResponse(ContactsPageJson(contacts.ToArray()));
        });
        var client = TestClientFactory.Create(handler);
        var directory = new FreeAgentContactDirectory(client);

        var results = await directory.SearchAsync("acme");

        Assert.Empty(results);
        Assert.Equal(5, pageCalls);
    }

    [Fact]
    public async Task GetAsync_ReturnsSome_WhenContactExists()
    {
        var handler = new StubHttpMessageHandler((request, index) => JsonResponse(ContactJson("Acme Widgets Ltd", null, null)));
        var client = TestClientFactory.Create(handler);
        var directory = new FreeAgentContactDirectory(client);

        var result = await directory.GetAsync(new FreeAgentContactIdentity(ContactUrl));

        var contact = Assert.IsType<FreeAgentContact>(result.Value);
        Assert.Equal("Acme Widgets Ltd", contact.DisplayName);
    }

    [Fact]
    public async Task GetAsync_ReturnsNone_WhenContactDoesNotExist()
    {
        var handler = new StubHttpMessageHandler((request, index) => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        var client = TestClientFactory.Create(handler);
        var directory = new FreeAgentContactDirectory(client);

        var result = await directory.GetAsync(new FreeAgentContactIdentity(ContactUrl));

        Assert.True(result is InvoiceManager.Core.None, $"Expected None but got {result}.");
    }

    private static string ContactsPageJson(params (string? Organisation, string? First, string? Last)[] contacts) =>
        $$"""
        {"contacts": [{{string.Join(",", contacts.Select(c => ContactBodyJson(c.Organisation, c.First, c.Last)))}}]}
        """;

    private static string ContactJson(string? organisation, string? first, string? last) =>
        $$"""{"contact": {{ContactBodyJson(organisation, first, last)}}}""";

    private static string ContactBodyJson(string? organisation, string? first, string? last) => $$"""
        {
          "url": "{{ContactUrl}}",
          "organisation_name": {{(organisation is null ? "null" : $"\"{organisation}\"")}},
          "first_name": {{(first is null ? "null" : $"\"{first}\"")}},
          "last_name": {{(last is null ? "null" : $"\"{last}\"")}}
        }
        """;

    private static string EmptyPageJson() => """{"contacts": []}""";

    private static HttpResponseMessage JsonResponse(string json) => new(System.Net.HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
    };
}
