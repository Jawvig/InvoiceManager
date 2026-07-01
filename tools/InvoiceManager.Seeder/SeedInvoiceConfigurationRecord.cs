namespace InvoiceManager.Seeder;

/// <summary>
/// The JSON shape used in <c>data/seed/invoice-configurations.json</c>.
/// Uses flat amount/currency fields so the seed file needs no custom converters.
/// </summary>
internal sealed record SeedInvoiceConfigurationRecord(
    string Id,
    string IntegrationType,
    string InvoiceDescription,
    string Frequency,
    decimal DefaultExpectedAmount,
    string DefaultExpectedCurrency,
    string DefaultVatMode,
    bool IsActive,
    string OneDriveDestination,
    string StartDate,
    string BillingAccountId,
    int DateToleranceDays);
