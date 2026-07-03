using NodaMoney;

namespace InvoiceManager.Core;

/// <summary>
/// A configuration entry describing an invoice that the service expects to
/// retrieve on a recurring basis.
/// </summary>
public sealed record InvoiceConfiguration(
    InvoiceConfigurationId Id,
    IntegrationType IntegrationType,
    string InvoiceDescription,
    InvoiceFrequency Frequency,
    Money DefaultExpectedAmount,
    VatMode DefaultVatMode,
    bool IsActive,
    string OneDriveDestination,
    DateOnly StartDate,
    string BillingAccountId,
    int DateToleranceDays,
    decimal AmountTolerance);
