using System.Text.Json.Serialization;

namespace InvoiceManager.Integrations.Microsoft365;

/// <summary>The Azure Billing "list invoices" response envelope.</summary>
internal sealed record BillingInvoiceListResponse(
    [property: JsonPropertyName("value")] IReadOnlyList<BillingInvoice> Value);

/// <summary>A single invoice. <c>Name</c> is the source invoice id (for example <c>G152207778</c>).</summary>
internal sealed record BillingInvoice(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("properties")] BillingInvoiceProperties Properties);

internal sealed record BillingInvoiceProperties(
    [property: JsonPropertyName("invoiceDate")] DateTimeOffset InvoiceDate,
    [property: JsonPropertyName("totalAmount")] BillingAmount TotalAmount,
    [property: JsonPropertyName("documents")] IReadOnlyList<BillingDocument>? Documents);

internal sealed record BillingAmount(
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("value")] decimal Value);

/// <summary>A downloadable document. <c>Kind</c> distinguishes "Invoice" from "CreditNote" etc.</summary>
internal sealed record BillingDocument(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("kind")] string Kind);

/// <summary>The final download response, once the async operation has completed.</summary>
internal sealed record DownloadDocumentsResponse(
    [property: JsonPropertyName("url")] string Url);
