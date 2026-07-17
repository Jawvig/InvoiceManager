using System.Text.Json.Serialization;

namespace InvoiceManager.Core;

/// <summary>
/// The configured identifier used to select the correct invoice source integration.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<IntegrationType>))]
public enum IntegrationType
{
    Microsoft365,
    Azure,
    Microsoft365Email,
}
