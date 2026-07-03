using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;

namespace InvoiceManager.Core;

/// <summary>
/// The unique identifier for an invoice record.
/// Format: <c>{configurationId}_{expectedDate:yyyy-MM-dd}</c>.
/// Serialises as a plain string in JSON and Cosmos DB.
/// </summary>
[TypeConverter(typeof(StringIdTypeConverter<InvoiceRecordId>))]
[JsonConverter(typeof(StringIdJsonConverter<InvoiceRecordId>))]
public sealed record InvoiceRecordId(string Value) : IStringId<InvoiceRecordId>
{
    public string Value { get; } = IsValidFormat(Value)
        ? Value
        : throw new ArgumentException(
            $"InvoiceRecordId must be in the format '{{configurationId}}_{{yyyy-MM-dd}}', got: '{Value}'.",
            nameof(Value));

    public static InvoiceRecordId NewId(DateOnly expectedDate, InvoiceConfigurationId configurationId) =>
        new($"{configurationId.Value}_{expectedDate.ToString("O", CultureInfo.InvariantCulture)}");

    public override string ToString() => Value;

    static InvoiceRecordId IStringId<InvoiceRecordId>.Create(string value) => new(value);

    // Valid format: at least one char for configurationId, then '_', then exactly 'yyyy-MM-dd'.
    // The date portion is always the last 10 characters, preceded by '_' at position [^11].
    private static bool IsValidFormat(string value) =>
        value.Length >= 12 &&
        value[^11] == '_' &&
        DateOnly.TryParseExact(value[^10..], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
