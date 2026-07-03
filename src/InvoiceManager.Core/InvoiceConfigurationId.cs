using System.ComponentModel;
using System.Text.Json.Serialization;

namespace InvoiceManager.Core;

/// <summary>
/// The stable slug that uniquely identifies an invoice configuration.
/// Serialises as a plain string in JSON and Cosmos DB.
/// </summary>
[TypeConverter(typeof(StringIdTypeConverter<InvoiceConfigurationId>))]
[JsonConverter(typeof(StringIdJsonConverter<InvoiceConfigurationId>))]
public sealed record InvoiceConfigurationId(string Value) : IStringId<InvoiceConfigurationId>
{
    public string Value { get; } = !string.IsNullOrEmpty(Value)
        ? Value
        : throw new ArgumentException("InvoiceConfigurationId cannot be null or empty.", nameof(Value));

    public override string ToString() => Value;

    static InvoiceConfigurationId IStringId<InvoiceConfigurationId>.Create(string value) => new(value);
}
