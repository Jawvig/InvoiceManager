using InvoiceManager.Core;
using InvoiceManager.Core.Integrations;

namespace InvoiceManager.TestSupport;

/// <summary>
/// A OneDrive integration test double that records upload requests and returns a
/// location derived from the destination path and filename.
/// </summary>
public sealed class FakeOneDriveIntegration : IOneDriveIntegration
{
    private readonly List<OneDriveUploadRequest> uploads = [];

    public IReadOnlyList<OneDriveUploadRequest> Uploads => uploads;

    public Task<OneDriveDetails> UploadAsync(
        OneDriveUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        uploads.Add(request);
        return Task.FromResult(new OneDriveDetails($"{request.DestinationPath}/{request.FileName}"));
    }
}
