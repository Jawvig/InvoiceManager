using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using InvoiceManager.Core;
using InvoiceManager.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace InvoiceManager.Infrastructure.MicrosoftAuthorization;

public sealed record BillingAccountChoice(string Id, string DisplayName, string AccountType);

/// <summary>A OneDrive drive belonging to the workflow account, as returned by <c>GET /me/drives</c>.</summary>
public sealed record OneDriveDriveChoice(string Id, string Name);

/// <summary>A single OneDrive folder returned by a one-level children listing.</summary>
public sealed record OneDriveFolderEntry(string Id, string Name);

public sealed class MicrosoftResourceDiscoveryOptions
{
    public const string SectionName = "MicrosoftBilling";
    public string ApiVersion { get; set; } = "2024-04-01";
}

public interface IMicrosoftResourceDiscovery
{
    Task<IReadOnlyList<BillingAccountChoice>> ListBillingAccountsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Lists all drives belonging to the workflow account (not just the default drive).</summary>
    Task<IReadOnlyList<OneDriveDriveChoice>> ListDrivesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the immediate folder children of <paramref name="folderItemId"/> (or the drive root
    /// when null) in a single Graph call. Callers drive folder navigation by calling this
    /// repeatedly as the user drills in, rather than walking the whole tree server-side.
    /// </summary>
    Task<IReadOnlyList<OneDriveFolderEntry>> ListFolderChildrenAsync(
        string driveId, string? folderItemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies that <paramref name="folderItemId"/> is a real, currently-existing folder in
    /// <paramref name="driveId"/> that the workflow account can access, resolving its
    /// authoritative drive name and full path via Graph rather than trusting posted display
    /// values. Returns <c>null</c> when the drive/item doesn't exist or isn't a folder.
    /// </summary>
    Task<OneDriveFolder?> GetFolderAsync(
        string driveId, string folderItemId, CancellationToken cancellationToken = default);
}

/// <summary>Discovers billing accounts using the captured workflow account.</summary>
public sealed class MicrosoftResourceDiscovery(
    HttpClient httpClient,
    IMicrosoftTokenProvider tokenProvider,
    IOptions<MicrosoftResourceDiscoveryOptions>? options = null) : IMicrosoftResourceDiscovery
{
    private const string BillingScope = "https://management.azure.com/user_impersonation";
    private const string GraphScope = "https://graph.microsoft.com/Files.ReadWrite.All";
    private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";
    private readonly MicrosoftResourceDiscoveryOptions settings =
        options?.Value ?? new MicrosoftResourceDiscoveryOptions();

    public async Task<IReadOnlyList<BillingAccountChoice>> ListBillingAccountsAsync(
        CancellationToken cancellationToken = default)
    {
        var token = await tokenProvider.AcquireTokenAsync([BillingScope], cancellationToken);
        var accounts = new List<BillingAccountChoice>();
        string? next =
            "https://management.azure.com/providers/Microsoft.Billing/billingAccounts" +
            $"?api-version={Uri.EscapeDataString(settings.ApiVersion)}";
        while (next is not null)
        {
            using var response = await SendAsync(next, token, cancellationToken);
            await response.EnsureSuccessAsync("Azure Billing", "listing billing accounts", cancellationToken);
            var page = await response.Content.ReadFromJsonAsync<BillingAccountPage>(cancellationToken);
            foreach (var account in page?.Value ?? [])
            {
                var id = account.Name ?? LastSegment(account.Id);
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                accounts.Add(new(id, account.Properties.DisplayName ?? "", account.Properties.AccountType));
            }
            next = page?.NextLink;
        }
        return accounts.OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id).ToList();
    }

    public async Task<IReadOnlyList<OneDriveDriveChoice>> ListDrivesAsync(
        CancellationToken cancellationToken = default)
    {
        var token = await tokenProvider.AcquireTokenAsync([GraphScope], cancellationToken);
        var drives = new List<OneDriveDriveChoice>();
        string? next = $"{GraphBaseUrl}/me/drives";
        while (next is not null)
        {
            using var response = await SendAsync(next, token, cancellationToken);
            await response.EnsureSuccessAsync("Microsoft Graph", "listing OneDrive drives", cancellationToken);
            var page = await response.Content.ReadFromJsonAsync<DrivePage>(cancellationToken);
            foreach (var drive in page?.Value ?? [])
            {
                if (string.IsNullOrWhiteSpace(drive.Id))
                    continue;
                var name = string.IsNullOrWhiteSpace(drive.Name) ? "OneDrive" : drive.Name;
                drives.Add(new(drive.Id, name));
            }
            next = page?.NextLink;
        }
        return drives;
    }

    public async Task<IReadOnlyList<OneDriveFolderEntry>> ListFolderChildrenAsync(
        string driveId, string? folderItemId, CancellationToken cancellationToken = default)
    {
        var token = await tokenProvider.AcquireTokenAsync([GraphScope], cancellationToken);
        var basePath = string.IsNullOrWhiteSpace(folderItemId)
            ? $"/drives/{Uri.EscapeDataString(driveId)}/root"
            : $"/drives/{Uri.EscapeDataString(driveId)}/items/{Uri.EscapeDataString(folderItemId)}";

        var folders = new List<OneDriveFolderEntry>();
        string? next = $"{GraphBaseUrl}{basePath}/children?$select=id,name,folder";
        while (next is not null)
        {
            using var response = await SendAsync(next, token, cancellationToken);
            await response.EnsureSuccessAsync("Microsoft Graph", "listing OneDrive folder children", cancellationToken);
            var page = await response.Content.ReadFromJsonAsync<FolderChildrenPage>(cancellationToken);
            foreach (var child in page?.Value ?? [])
            {
                // Only directories have a "folder" facet; files are excluded client-side since
                // Graph's $filter=folder ne null is not reliably supported on children listings.
                if (child.Folder is null || string.IsNullOrWhiteSpace(child.Id) || string.IsNullOrWhiteSpace(child.Name))
                    continue;
                folders.Add(new(child.Id, child.Name));
            }
            next = page?.NextLink;
        }
        return folders.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<OneDriveFolder?> GetFolderAsync(
        string driveId, string folderItemId, CancellationToken cancellationToken = default)
    {
        var token = await tokenProvider.AcquireTokenAsync([GraphScope], cancellationToken);

        var itemUrl = $"{GraphBaseUrl}/drives/{Uri.EscapeDataString(driveId)}/items/{Uri.EscapeDataString(folderItemId)}" +
            "?$select=id,name,folder,parentReference";
        using var itemResponse = await SendAsync(itemUrl, token, cancellationToken);
        if (IsInvalidSelection(itemResponse.StatusCode))
            return null;
        await itemResponse.EnsureSuccessAsync("Microsoft Graph", "verifying a OneDrive folder selection", cancellationToken);
        var item = await itemResponse.Content.ReadFromJsonAsync<FolderItemResource>(cancellationToken);
        if (item?.Folder is null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Name))
            return null;

        var driveUrl = $"{GraphBaseUrl}/drives/{Uri.EscapeDataString(driveId)}?$select=name";
        using var driveResponse = await SendAsync(driveUrl, token, cancellationToken);
        if (IsInvalidSelection(driveResponse.StatusCode))
            return null;
        await driveResponse.EnsureSuccessAsync("Microsoft Graph", "verifying a OneDrive folder selection", cancellationToken);
        var drive = await driveResponse.Content.ReadFromJsonAsync<DriveResource>(cancellationToken);
        var driveName = string.IsNullOrWhiteSpace(drive?.Name) ? "OneDrive" : drive!.Name!;

        // Leading "/" matches the canonical format used elsewhere (onedrive-picker.js's
        // commitSelection() and the seed data), so a freshly verified selection displays/audits
        // identically to the same folder already stored on a configuration.
        var relativePath = ExtractRelativePath(item.ParentReference?.Path);
        var folderPath = string.IsNullOrEmpty(relativePath) ? $"/{item.Name}" : $"/{relativePath}/{item.Name}";

        return new OneDriveFolder(driveId, driveName, item.Id, folderPath);
    }

    // A malformed or forged drive/item ID commonly comes back as 400 (Graph rejects the ID
    // syntax before it can even look anything up), not 404 — both mean "not a valid selection"
    // from this caller's point of view. Genuine auth/server failures (401/403/5xx) still fall
    // through to EnsureSuccessAsync and throw, since those aren't the caller's fault to fix by
    // picking a different folder.
    private static bool IsInvalidSelection(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest;

    // parentReference.path looks like "/drives/{id}/root:/Bills" (or "/drives/{id}/root:" at
    // the drive root, with no trailing segment) — strip everything up to and including "root:".
    private static string ExtractRelativePath(string? graphPath)
    {
        if (string.IsNullOrEmpty(graphPath))
            return "";
        const string marker = "root:";
        var index = graphPath.IndexOf(marker, StringComparison.Ordinal);
        return index < 0 ? "" : graphPath[(index + marker.Length)..].Trim('/');
    }

    private async Task<HttpResponseMessage> SendAsync(string url, string token, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await httpClient.SendAsync(request, cancellationToken);
    }

    private static string LastSegment(string? value) =>
        value?.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";

    private sealed record DrivePage(
        [property: JsonPropertyName("value")] IReadOnlyList<DriveResource> Value,
        [property: JsonPropertyName("@odata.nextLink")] string? NextLink);
    private sealed record DriveResource(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name);

    private sealed record FolderChildrenPage(
        [property: JsonPropertyName("value")] IReadOnlyList<FolderChildResource> Value,
        [property: JsonPropertyName("@odata.nextLink")] string? NextLink);
    private sealed record FolderChildResource(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("folder")] object? Folder);

    private sealed record FolderItemResource(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("folder")] object? Folder,
        [property: JsonPropertyName("parentReference")] ParentReferenceResource? ParentReference);
    private sealed record ParentReferenceResource(
        [property: JsonPropertyName("path")] string? Path);

    private sealed record BillingAccountPage(
        [property: JsonPropertyName("value")] IReadOnlyList<BillingAccountResource> Value,
        [property: JsonPropertyName("nextLink")] string? NextLink);
    private sealed record BillingAccountResource(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("properties")] BillingAccountProperties Properties);
    private sealed record BillingAccountProperties(
        [property: JsonPropertyName("accountType")] string AccountType,
        [property: JsonPropertyName("displayName")] string? DisplayName);
}
