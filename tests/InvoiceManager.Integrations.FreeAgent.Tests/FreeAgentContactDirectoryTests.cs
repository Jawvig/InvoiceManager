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
    public async Task SearchAsync_ScansAtMostFiftyPages_WhenNoMatchesFound()
    {
        var pageCalls = 0;
        var handler = new StubHttpMessageHandler((request, index) =>
        {
            pageCalls++;
            // Every page is full (100 non-matching contacts) so paging never naturally stops -
            // the page cap is the only thing that bounds this (a runaway-loop guard, not a
            // usability limit - see MaxPagesScanned's comment).
            var contacts = Enumerable.Range(0, 100).Select(_ => ((string?)"No Match", (string?)null, (string?)null));
            return JsonResponse(ContactsPageJson(contacts.ToArray()));
        });
        var client = TestClientFactory.Create(handler);
        var directory = new FreeAgentContactDirectory(client);

        var results = await directory.SearchAsync("acme");

        Assert.Empty(results);
        Assert.Equal(50, pageCalls);
    }

    [Fact]
    public async Task SearchAsync_FindsMatch_PastThePreviousLowerPageCap()
    {
        // Regression guard: contacts sorted alphabetically past the fifth page (the old cap) must
        // still be reachable, since FreeAgent has no server-side text filter to narrow which pages
        // get fetched - a match here proves the cap is now a generous safety bound, not a
        // "refine your query" usability limit that silently made later contacts unreachable.
        var handler = new StubHttpMessageHandler((request, index) =>
            index switch
            {
                < 6 => JsonResponse(ContactsPageJson(
                    Enumerable.Range(0, 100).Select(_ => ((string?)"No Match", (string?)null, (string?)null)).ToArray())),
                6 => JsonResponse(ContactsPageJson(("Zzyzx Corp", null, null))),
                _ => JsonResponse(EmptyPageJson()),
            });
        var client = TestClientFactory.Create(handler);
        var directory = new FreeAgentContactDirectory(client);

        var results = await directory.SearchAsync("zzyzx");

        var contact = Assert.Single(results);
        Assert.Equal("Zzyzx Corp", contact.DisplayName);
    }

    [Fact]
    public async Task SearchAsync_MatchesCombinedPersonName()
    {
        // The picker displays "Jane Smith" (see ContactWireExtensions.DisplayName), so a search
        // for that full displayed name must match even though neither FirstName nor LastName
        // alone contains it.
        var handler = new StubHttpMessageHandler((request, index) =>
            index switch
            {
                0 => JsonResponse(ContactsPageJson((null, "Jane", "Smith"))),
                _ => JsonResponse(EmptyPageJson()),
            });
        var client = TestClientFactory.Create(handler);
        var directory = new FreeAgentContactDirectory(client);

        var results = await directory.SearchAsync("jane smith");

        var contact = Assert.Single(results);
        Assert.Equal("Jane Smith", contact.DisplayName);
    }

    [Fact]
    public async Task GetAsync_ReturnsNone_WhenApiClientThrows()
    {
        // A stored/imported contact URL can be syntactically valid but wrong for this environment
        // (e.g. a sandbox URL posted against a production deployment) - FreeAgentApiClient's host
        // allowlist rejects that with an exception rather than an HTTP response. GetAsync must
        // translate it into the same "doesn't resolve here" outcome as a 404, not let it surface
        // as an unhandled exception to the caller (AdminWeb's Edit/Import save handler).
        var handler = new StubHttpMessageHandler((request, index) => throw new InvalidOperationException("Refusing to call FreeAgent host."));
        var client = TestClientFactory.Create(handler);
        var directory = new FreeAgentContactDirectory(client);

        var result = await directory.GetAsync(new FreeAgentContactIdentity(ContactUrl));

        Assert.True(result is InvoiceManager.Core.None, $"Expected None but got {result}.");
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
