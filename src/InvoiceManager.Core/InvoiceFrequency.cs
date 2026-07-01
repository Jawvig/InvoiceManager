using System.Text.Json.Serialization;

namespace InvoiceManager.Core;

/// <summary>
/// The recurrence pattern for an expected invoice. Only the frequencies needed
/// by the configured invoices are supported initially.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<InvoiceFrequency>))]
public enum InvoiceFrequency
{
    Monthly,
}
