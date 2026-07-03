using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InvoiceManager.Core;

/// <summary>
/// A typed identifier wrapping a single string, round-tripping as a plain
/// string in JSON and binding frameworks via <see cref="StringIdJsonConverter{TId}"/>
/// and <see cref="StringIdTypeConverter{TId}"/>. Implementations validate in
/// their constructor; <see cref="Create"/> simply invokes it.
/// </summary>
public interface IStringId<TSelf> where TSelf : IStringId<TSelf>
{
    string Value { get; }

    static abstract TSelf Create(string value);
}

/// <summary>Serialises a typed string ID as a plain JSON string.</summary>
public sealed class StringIdJsonConverter<TId> : JsonConverter<TId>
    where TId : IStringId<TId>
{
    public override TId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        TId.Create(reader.GetString()
            ?? throw new JsonException($"{typeof(TId).Name} cannot be null."));

    public override void Write(Utf8JsonWriter writer, TId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

/// <summary>Converts a typed string ID to and from a plain string for binding frameworks.</summary>
public sealed class StringIdTypeConverter<TId> : TypeConverter
    where TId : IStringId<TId>
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string s ? TId.Create(s) : base.ConvertFrom(context, culture, value);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
        destinationType == typeof(string) && value is TId id
            ? id.Value
            : base.ConvertTo(context, culture, value, destinationType);
}
