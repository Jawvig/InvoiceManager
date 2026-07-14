using InvoiceManager.Core;
using InvoiceManager.Core.Integrations;

namespace InvoiceManager.TestSupport;

/// <summary>
/// A OneDrive integration test double that records upload and search requests.
/// Uploads return a location derived from the destination path and filename.
/// Searches return <see cref="NoOneDriveMatch"/> by default (so tests that do not
/// exercise reconciliation behave as before); seed <see cref="NextSearchResult"/>
/// or <see cref="SearchException"/> to drive a specific outcome.
/// </summary>
public sealed class FakeOneDriveIntegration : IOneDriveIntegration
{
    private readonly List<OneDriveUploadRequest> uploads = [];
    private readonly List<OneDriveSearchRequest> searches = [];

    public IReadOnlyList<OneDriveUploadRequest> Uploads => uploads;

    public IReadOnlyList<OneDriveSearchRequest> Searches => searches;

    /// <summary>The result the next (and every subsequent) search returns. Defaults to no match.</summary>
    public OneDriveSearchResult NextSearchResult { get; set; } = new NoOneDriveMatch();

    /// <summary>When set, <see cref="SearchAsync"/> throws this to simulate a technical failure.</summary>
    public Exception? SearchException { get; set; }

    public Task<OneDriveDetails> UploadAsync(
        OneDriveUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        uploads.Add(request);
        return Task.FromResult(new OneDriveDetails($"{request.DestinationPath}/{request.FileName}"));
    }

    public Task<OneDriveSearchResult> SearchAsync(
        OneDriveSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        searches.Add(request);
        if (SearchException is not null)
            throw SearchException;
        return Task.FromResult(NextSearchResult);
    }
}
