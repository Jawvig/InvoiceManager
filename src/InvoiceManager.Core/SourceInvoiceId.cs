using System.ComponentModel;
using System.Text.Json.Serialization;

namespace InvoiceManager.Core;

/// <summary>
/// The identifier a source system assigns to an invoice (for Microsoft 365 this
/// is the Azure Billing invoice <c>name</c>, for example <c>G152207778</c>).
/// Serialises as a plain string in JSON and Cosmos DB.
/// </summary>
[TypeConverter(typeof(StringIdTypeConverter<SourceInvoiceId>))]
[JsonConverter(typeof(StringIdJsonConverter<SourceInvoiceId>))]
public sealed record SourceInvoiceId(string Value) : IStringId<SourceInvoiceId>
{
    public string Value { get; } = !string.IsNullOrEmpty(Value)
        ? Value
        : throw new ArgumentException("SourceInvoiceId cannot be null or empty.", nameof(Value));

    public override string ToString() => Value;

    static SourceInvoiceId IStringId<SourceInvoiceId>.Create(string value) => new(value);
}
