using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InvoiceManager.Core;

/// <summary>
/// The unique identifier for an invoice record.
/// Format: <c>{configurationId}_{expectedDate:yyyy-MM-dd}</c>.
/// Serialises as a plain string in JSON and Cosmos DB.
/// </summary>
[TypeConverter(typeof(InvoiceRecordIdTypeConverter))]
[JsonConverter(typeof(InvoiceRecordIdJsonConverter))]
public sealed record InvoiceRecordId(string Value)
{
    public string Value { get; } = IsValidFormat(Value)
        ? Value
        : throw new ArgumentException(
            $"InvoiceRecordId must be in the format '{{configurationId}}_{{yyyy-MM-dd}}', got: '{Value}'.",
            nameof(Value));

    public static InvoiceRecordId NewId(DateOnly expectedDate, InvoiceConfigurationId configurationId) =>
        new($"{configurationId.Value}_{expectedDate:yyyy-MM-dd}");

    public override string ToString() => Value;

    // Valid format: at least one char for configurationId, then '_', then exactly 'yyyy-MM-dd'.
    // The date portion is always the last 10 characters, preceded by '_' at position [^11].
    private static bool IsValidFormat(string value) =>
        value.Length >= 12 &&
        value[^11] == '_' &&
        DateOnly.TryParseExact(value[^10..], "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out _);

    private sealed class InvoiceRecordIdTypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value) =>
            value is string s ? new InvoiceRecordId(s) : base.ConvertFrom(context, culture, value);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
            destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object? ConvertTo(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object? value, Type destinationType) =>
            destinationType == typeof(string) && value is InvoiceRecordId id
                ? id.Value
                : base.ConvertTo(context, culture, value, destinationType);
    }

    private sealed class InvoiceRecordIdJsonConverter : JsonConverter<InvoiceRecordId>
    {
        public override InvoiceRecordId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(reader.GetString() ?? throw new JsonException("InvoiceRecordId cannot be null."));

        public override void Write(Utf8JsonWriter writer, InvoiceRecordId value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Value);
    }
}
