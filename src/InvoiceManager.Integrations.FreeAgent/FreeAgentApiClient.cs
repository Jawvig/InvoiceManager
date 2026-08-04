using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using InvoiceManager.Infrastructure.FreeAgentAuthorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvoiceManager.Integrations.FreeAgent;

/// <summary>The result of a FreeAgent API call that could be rejected because a field is locked.</summary>
internal sealed record FreeAgentApiResult<T>(T? Value, bool IsLocked, string? LockedFieldDetail, HttpStatusCode StatusCode)
{
    public bool Succeeded => Value is not null;
}

/// <summary>
/// Internal HTTP wrapper around the FreeAgent v2 REST API. Enforces the
/// sandbox/production host allowlist at construction time (production code can
/// never be pointed at an unrecognised host), attaches a bearer token via
/// <see cref="IFreeAgentTokenProvider"/>, and translates the proven 422
/// locked-field response into a typed result at this exact boundary rather than
/// letting callers parse FreeAgent's error shape themselves.
/// </summary>
internal sealed class FreeAgentApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;
    private readonly IFreeAgentTokenProvider tokenProvider;
    private readonly ILogger<FreeAgentApiClient> logger;

    public FreeAgentApiClient(
        HttpClient httpClient,
        IFreeAgentTokenProvider tokenProvider,
        IOptions<FreeAgentOptions> freeAgentOptions,
        ILogger<FreeAgentApiClient> logger)
    {
        this.httpClient = httpClient;
        this.tokenProvider = tokenProvider;
        this.logger = logger;

        var environment = freeAgentOptions.Value.Environment;
        httpClient.BaseAddress = FreeAgentHosts.ApiBaseUri(environment);
    }

    public async Task<IReadOnlyList<BillWire>> GetBillsPageAsync(
        DateOnly fromDate, DateOnly toDate, string contactUrl, int page, int perPage, CancellationToken cancellationToken)
    {
        var url =
            $"bills?nested_bill_items=true" +
            $"&from_date={fromDate:yyyy-MM-dd}&to_date={toDate:yyyy-MM-dd}" +
            $"&contact={Uri.EscapeDataString(contactUrl)}" +
            $"&page={page}&per_page={perPage}";

        using var response = await SendAsync(HttpMethod.Get, url, content: null, cancellationToken);
        await EnsureSuccessAsync(response, "listing bills", cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<BillsResponseWire>(SerializerOptions, cancellationToken);
        return body?.Bills ?? [];
    }

    public async Task<BillWire> GetBillAsync(string billUrl, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"{billUrl}?nested_bill_items=true", content: null, cancellationToken);
        await EnsureSuccessAsync(response, "reading a bill", cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<BillResponseWire>(SerializerOptions, cancellationToken);
        return body?.Bill ?? throw new InvalidOperationException("FreeAgent returned no bill body.");
    }

    public async Task<FreeAgentApiResult<BillWire>> PutBillDateAsync(
        string billUrl, DateOnly datedOn, CancellationToken cancellationToken)
    {
        var payload = new { bill = new { dated_on = datedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) } };
        return await PutBillAsync(billUrl, payload, cancellationToken);
    }

    public async Task<FreeAgentApiResult<BillWire>> PutBillItemAmountAsync(
        string billUrl, string itemUrl, decimal totalValue, CancellationToken cancellationToken)
    {
        var payload = new
        {
            bill = new
            {
                bill_items = new[]
                {
                    new { url = itemUrl, total_value = totalValue.ToString("0.00", CultureInfo.InvariantCulture) },
                },
            },
        };
        return await PutBillAsync(billUrl, payload, cancellationToken);
    }

    private async Task<FreeAgentApiResult<BillWire>> PutBillAsync(string billUrl, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await SendAsync(HttpMethod.Put, billUrl, content, cancellationToken);

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var lockedDetail = ExtractLockedFieldDetail(errorBody);
            return new FreeAgentApiResult<BillWire>(default, IsLocked: true, lockedDetail, response.StatusCode);
        }

        await EnsureSuccessAsync(response, "updating a bill", cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<BillResponseWire>(SerializerOptions, cancellationToken);
        return new FreeAgentApiResult<BillWire>(body?.Bill, IsLocked: false, null, response.StatusCode);
    }

    public async Task<AttachmentWire> PostAttachmentAsync(
        string billUrl, byte[] pdfBytes, string fileName, CancellationToken cancellationToken)
    {
        var payload = new
        {
            attachment = new
            {
                data = Convert.ToBase64String(pdfBytes),
                file_name = fileName,
                content_type = "application/pdf",
            },
        };
        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await SendAsync(HttpMethod.Put, $"{billUrl}/attachment", content, cancellationToken);
        await EnsureSuccessAsync(response, "uploading a bill attachment", cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<BillResponseWire>(SerializerOptions, cancellationToken);
        return body?.Bill?.Attachment
            ?? throw new InvalidOperationException("FreeAgent's attachment upload response did not include attachment metadata.");
    }

    public async Task<IReadOnlyList<string>> GetBankAccountUrlsAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, "bank_accounts", content: null, cancellationToken);
        await EnsureSuccessAsync(response, "listing bank accounts", cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<BankAccountsResponseWire>(SerializerOptions, cancellationToken);
        return body?.BankAccounts.Select(a => a.Url).OfType<string>().ToList() ?? [];
    }

    public async Task<IReadOnlyList<BankTransactionExplanationWire>> GetExplanationsAsync(
        string bankAccountUrl, CancellationToken cancellationToken)
    {
        var url = $"bank_transaction_explanations?bank_account={Uri.EscapeDataString(bankAccountUrl)}";
        using var response = await SendAsync(HttpMethod.Get, url, content: null, cancellationToken);
        await EnsureSuccessAsync(response, "listing bank transaction explanations", cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<BankTransactionExplanationsResponseWire>(SerializerOptions, cancellationToken);
        return body?.Explanations ?? [];
    }

    public async Task DeleteExplanationAsync(string explanationUrl, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Delete, explanationUrl, content: null, cancellationToken);
        await EnsureSuccessAsync(response, "deleting a bank transaction explanation", cancellationToken);
    }

    public async Task<BankTransactionWire> GetBankTransactionAsync(string bankTransactionUrl, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, bankTransactionUrl, content: null, cancellationToken);
        await EnsureSuccessAsync(response, "reading a bank transaction", cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<BankTransactionResponseWire>(SerializerOptions, cancellationToken);
        return body?.BankTransaction ?? throw new InvalidOperationException("FreeAgent returned no bank transaction body.");
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string url, HttpContent? content, CancellationToken cancellationToken)
    {
        var token = await tokenProvider.AcquireTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await httpClient.SendAsync(request, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        // Never log the response body: FreeAgent error bodies can echo request parameters,
        // and this boundary must never leak tokens/secrets into logs or exceptions.
        throw new InvalidOperationException(
            $"FreeAgent request failed while {action}: {(int)response.StatusCode} {response.ReasonPhrase}.");
    }

    /// <summary>
    /// Looks for the proven locked-field substrings ("cached_total_value" /
    /// "bill_items.total_value") in a 422 body. Returns a short, redacted detail
    /// string - never the raw body, which could contain other request context.
    /// </summary>
    private static string ExtractLockedFieldDetail(string errorBody)
    {
        if (errorBody.Contains("cached_total_value", StringComparison.OrdinalIgnoreCase))
            return "cached_total_value is locked";
        if (errorBody.Contains("bill_items.total_value", StringComparison.OrdinalIgnoreCase))
            return "bill_items.total_value is locked";
        return "locked (unrecognised field)";
    }
}
