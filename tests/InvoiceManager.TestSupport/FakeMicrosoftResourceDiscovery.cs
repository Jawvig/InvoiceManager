using InvoiceManager.Core;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;

namespace InvoiceManager.TestSupport;

/// <summary>A scriptable <see cref="IMicrosoftResourceDiscovery"/> test double for AdminWeb page
/// handler tests, avoiding the need to mock the underlying HttpClient/token-provider plumbing.</summary>
public sealed class FakeMicrosoftResourceDiscovery(
    IReadOnlyList<BillingAccountChoice>? billingAccounts = null,
    IReadOnlyList<OneDriveDriveChoice>? drives = null,
    IReadOnlyList<OneDriveFolderEntry>? folderChildren = null,
    OneDriveFolder? verifiedFolder = null) : IMicrosoftResourceDiscovery
{
    public Task<IReadOnlyList<BillingAccountChoice>> ListBillingAccountsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(billingAccounts ?? []);

    public Task<IReadOnlyList<OneDriveDriveChoice>> ListDrivesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(drives ?? []);

    public Task<IReadOnlyList<OneDriveFolderEntry>> ListFolderChildrenAsync(
        string driveId, string? folderItemId, CancellationToken cancellationToken = default) =>
        Task.FromResult(folderChildren ?? []);

    public Task<OneDriveFolder?> GetFolderAsync(
        string driveId, string folderItemId, CancellationToken cancellationToken = default) =>
        Task.FromResult(verifiedFolder);
}
