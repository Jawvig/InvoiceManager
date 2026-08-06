using InvoiceManager.Core;
using InvoiceManager.Core.Integrations.FreeAgent;

namespace InvoiceManager.TestSupport;

public sealed class FakeFreeAgentContactDirectory : IFreeAgentContactDirectory
{
    public IReadOnlyList<FreeAgentContact> SearchResults { get; set; } = [];

    public Option<FreeAgentContact> GetResult { get; set; } = Option.None;

    /// <summary>When set, GetAsync throws this instead of returning <see cref="GetResult"/> -
    /// simulates a system error (as distinct from a genuine not-found) for tests covering
    /// ConfigurationFormPageModel.RefreshFreeAgentContactAsync's error-vs-not-found split.</summary>
    public Exception? GetException { get; set; }

    public List<string> SearchQueries { get; } = [];

    public List<FreeAgentContactIdentity> GetRequests { get; } = [];

    public Task<IReadOnlyList<FreeAgentContact>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        SearchQueries.Add(query);
        return Task.FromResult(SearchResults);
    }

    public Task<Option<FreeAgentContact>> GetAsync(FreeAgentContactIdentity url, CancellationToken cancellationToken = default)
    {
        GetRequests.Add(url);
        if (GetException is { } exception) throw exception;
        return Task.FromResult(GetResult);
    }
}
