using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using InvoiceManager.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace InvoiceManager.Infrastructure.MicrosoftAuthorization;

public sealed record BillingAccountChoice(string Id, string Label, string AccountType);

public sealed class MicrosoftResourceDiscoveryOptions
{
    public const string SectionName = "MicrosoftBilling";
    public string ApiVersion { get; set; } = "2024-04-01";
}

public interface IMicrosoftResourceDiscovery
{
    Task<IReadOnlyList<BillingAccountChoice>> ListBillingAccountsAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>Discovers billing accounts using the captured workflow account.</summary>
public sealed class MicrosoftResourceDiscovery(
    HttpClient httpClient,
    IMicrosoftTokenProvider tokenProvider,
    IOptions<MicrosoftResourceDiscoveryOptions>? options = null) : IMicrosoftResourceDiscovery
{
    private const string BillingScope = "https://management.azure.com/user_impersonation";
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
                var label = string.IsNullOrWhiteSpace(account.Properties.DisplayName)
                    ? id
                    : $"{account.Properties.DisplayName} ({id})";
                accounts.Add(new(id, label, account.Properties.AccountType));
            }
            next = page?.NextLink;
        }
        return accounts.OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase).ToList();
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
}
