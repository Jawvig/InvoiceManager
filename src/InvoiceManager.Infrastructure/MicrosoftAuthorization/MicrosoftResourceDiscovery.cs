using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using InvoiceManager.Core;
using InvoiceManager.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace InvoiceManager.Infrastructure.MicrosoftAuthorization;

public sealed record BillingAccountChoice(string Id, string Label, string AccountType);
public sealed record OneDriveFolderChoice(OneDriveDestination Destination, string Name);

public sealed class MicrosoftResourceDiscoveryOptions
{
    public const string SectionName = "MicrosoftBilling";
    public string ApiVersion { get; set; } = "2024-04-01";
}

public interface IMicrosoftResourceDiscovery
{
    Task<IReadOnlyList<BillingAccountChoice>> ListBillingAccountsAsync(
        IntegrationType integrationType,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OneDriveFolderChoice>> ListOneDriveFoldersAsync(
        CancellationToken cancellationToken = default);

    Task<Option<OneDriveDestination>> ResolveLegacyOneDrivePathAsync(
        string legacyPath,
        CancellationToken cancellationToken = default);
}

/// <summary>Discovers billing accounts and existing folders using the captured workflow account.</summary>
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
        IntegrationType integrationType,
        CancellationToken cancellationToken = default)
    {
        var requiredType = integrationType switch
        {
            IntegrationType.Microsoft365 => "Business",
            IntegrationType.Azure => "Individual",
            _ => throw new ArgumentException($"Billing account discovery is not supported for {integrationType}.")
        };
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
                if (!string.Equals(account.Properties.AccountType, requiredType, StringComparison.OrdinalIgnoreCase))
                    continue;
                var id = account.Name ?? LastSegment(account.Id);
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                var label = string.IsNullOrWhiteSpace(account.Properties.DisplayName)
                    ? id
                    : $"{account.Properties.DisplayName} ({id})";
                accounts.Add(new(id, label, account.Properties.AccountType));
            }
            next = page?.NextLink;
        }
        return accounts.OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<OneDriveFolderChoice>> ListOneDriveFoldersAsync(
        CancellationToken cancellationToken = default)
    {
        var token = await tokenProvider.AcquireTokenAsync([GraphScope], cancellationToken);
        using var driveResponse = await SendAsync($"{GraphBaseUrl}/me/drive?$select=id,name", token, cancellationToken);
        await driveResponse.EnsureSuccessAsync("Microsoft Graph", "reading the default OneDrive", cancellationToken);
        var drive = await driveResponse.Content.ReadFromJsonAsync<DriveResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Microsoft Graph returned no default drive.");

        var results = new List<OneDriveFolderChoice>();
        var queue = new Queue<(string? ItemId, string Path)>();
        queue.Enqueue((null, "/"));
        while (queue.TryDequeue(out var parent))
        {
            string? next = parent.ItemId is null
                ? $"{GraphBaseUrl}/drives/{Uri.EscapeDataString(drive.Id)}/root/children?$select=id,name,folder"
                : $"{GraphBaseUrl}/drives/{Uri.EscapeDataString(drive.Id)}/items/{Uri.EscapeDataString(parent.ItemId)}/children?$select=id,name,folder";
            while (next is not null)
            {
                using var response = await SendAsync(next, token, cancellationToken);
                await response.EnsureSuccessAsync("Microsoft Graph", "browsing OneDrive folders", cancellationToken);
                var page = await response.Content.ReadFromJsonAsync<DriveChildrenPage>(cancellationToken);
                foreach (var child in page?.Value ?? [])
                {
                    if (child.Folder is null || string.IsNullOrWhiteSpace(child.Id) || string.IsNullOrWhiteSpace(child.Name))
                        continue;
                    var path = parent.Path == "/" ? $"/{child.Name}" : $"{parent.Path}/{child.Name}";
                    var destination = new OneDriveDestination(path, drive.Id, child.Id);
                    results.Add(new(destination, child.Name));
                    queue.Enqueue((child.Id, path));
                }
                next = page?.NextLink;
            }
        }
        return results.OrderBy(x => x.Destination.DisplayPath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<Option<OneDriveDestination>> ResolveLegacyOneDrivePathAsync(
        string legacyPath,
        CancellationToken cancellationToken = default)
    {
        var token = await tokenProvider.AcquireTokenAsync([GraphScope], cancellationToken);
        using var response = await SendAsync($"{GraphBaseUrl}{legacyPath}", token, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return Option.None;
        await response.EnsureSuccessAsync("Microsoft Graph", "resolving the legacy OneDrive folder", cancellationToken);
        var item = await response.Content.ReadFromJsonAsync<ResolvedDriveItem>(cancellationToken);
        if (item is null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.ParentReference?.DriveId))
            return Option.None;
        var rootMarker = legacyPath.IndexOf("root:", StringComparison.OrdinalIgnoreCase);
        var displayPath = rootMarker >= 0 ? legacyPath[(rootMarker + "root:".Length)..] : legacyPath;
        if (string.IsNullOrWhiteSpace(displayPath)) displayPath = "/";
        return new OneDriveDestination(displayPath, item.ParentReference.DriveId, item.Id);
    }

    private async Task<HttpResponseMessage> SendAsync(string url, string token, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await httpClient.SendAsync(request, cancellationToken);
    }

    private static string LastSegment(string? value) =>
        value?.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";

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
    private sealed record DriveResponse([property: JsonPropertyName("id")] string Id);
    private sealed record DriveChildrenPage(
        [property: JsonPropertyName("value")] IReadOnlyList<DriveFolderItem> Value,
        [property: JsonPropertyName("@odata.nextLink")] string? NextLink);
    private sealed record DriveFolderItem(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("folder")] object? Folder);
    private sealed record ResolvedDriveItem(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("parentReference")] ParentReference? ParentReference);
    private sealed record ParentReference([property: JsonPropertyName("driveId")] string? DriveId);
}
