using InvoiceManager.Core;
using InvoiceManager.Core.Integrations.FreeAgent;

namespace InvoiceManager.Integrations.FreeAgent;

/// <summary>
/// Implements contact search/lookup against FreeAgent's contacts endpoint.
/// FreeAgent has no free-text search parameter on <c>GET /v2/contacts</c> (only
/// view/sort/updated_since filters), so <see cref="SearchAsync"/> pages through
/// contacts and filters client-side - the same "server does what it can, filter
/// the rest" split used for bill matching's amount-tolerance/reference filtering.
/// </summary>
internal sealed class FreeAgentContactDirectory(FreeAgentApiClient client) : IFreeAgentContactDirectory
{
    private const int PageSize = 100;
    private const int MaxPagesScanned = 5;
    private const int MaxResults = 20;

    public async Task<IReadOnlyList<FreeAgentContact>> SearchAsync(
        string query, CancellationToken cancellationToken = default)
    {
        var results = new List<FreeAgentContact>();
        var page = 1;
        while (results.Count < MaxResults && page <= MaxPagesScanned)
        {
            var pageResults = await client.SearchContactsPageAsync(page, PageSize, cancellationToken);
            if (pageResults.Count == 0)
                break;

            foreach (var wire in pageResults)
            {
                if (Matches(wire, query))
                {
                    results.Add(wire.ToContact());
                    if (results.Count == MaxResults)
                        break;
                }
            }

            if (pageResults.Count < PageSize)
                break;

            page++;
        }

        return results;
    }

    public async Task<Option<FreeAgentContact>> GetAsync(
        FreeAgentContactIdentity url, CancellationToken cancellationToken = default)
    {
        var wire = await client.GetContactAsync(url.Url.OriginalString, cancellationToken);
        return wire is not null ? wire.ToContact() : Option.None;
    }

    private static bool Matches(ContactWire wire, string query) =>
        Contains(wire.OrganisationName, query) || Contains(wire.FirstName, query) || Contains(wire.LastName, query);

    private static bool Contains(string? value, string query) =>
        value is not null && value.Contains(query, StringComparison.OrdinalIgnoreCase);
}
