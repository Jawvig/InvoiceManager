using System.Text.Json.Serialization;

namespace InvoiceManager.Seeder;

/// <summary>
/// The JSON shape used in <c>data/seed/invoice-configurations.json</c>.
/// Uses an optional nested amount-matching object.
/// </summary>
internal sealed record SeedInvoiceConfigurationRecord(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string IntegrationType,
    [property: JsonRequired] string InvoiceDescription,
    [property: JsonRequired] string Frequency,
    SeedAmountMatchingCriteria? AmountMatchingCriteria,
    [property: JsonRequired] string DefaultVatMode,
    [property: JsonRequired] bool IsActive,
    [property: JsonRequired] string OneDriveDestination,
    [property: JsonRequired] string StartDate,
    [property: JsonRequired] string BillingAccountId,
    [property: JsonRequired] int DateToleranceDays);

internal sealed record SeedAmountMatchingCriteria(
    [property: JsonRequired] decimal Amount,
    [property: JsonRequired] string Currency,
    [property: JsonRequired] decimal AmountTolerance);
