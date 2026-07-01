using System.Text.Json.Serialization;

namespace InvoiceManager.Core;

/// <summary>
/// Indicates whether an invoice total is VAT inclusive or VAT exclusive.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<VatMode>))]
public enum VatMode
{
    Inclusive,
    Exclusive,
}
