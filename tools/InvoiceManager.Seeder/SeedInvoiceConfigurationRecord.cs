using System.Text.Json.Serialization;

namespace InvoiceManager.Seeder;

/// <summary>
/// The JSON shape used in <c>data/seed/invoice-configurations.json</c>.
/// Uses flat amount/currency fields so the seed file needs no custom converters.
/// </summary>
internal sealed record SeedInvoiceConfigurationRecord(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string IntegrationType,
    [property: JsonRequired] string InvoiceDescription,
    [property: JsonRequired] string Frequency,
    [property: JsonRequired] decimal DefaultExpectedAmount,
    [property: JsonRequired] string DefaultExpectedCurrency,
    [property: JsonRequired] string DefaultVatMode,
    [property: JsonRequired] bool IsActive,
    [property: JsonRequired] string OneDriveDestination,
    [property: JsonRequired] string StartDate,
    [property: JsonRequired] string BillingAccountId,
    [property: JsonRequired] int DateToleranceDays,
    decimal AmountTolerance = 0m);
