using InvoiceManager.Core;
using NodaMoney;

namespace InvoiceManager.TestSupport;

public static class Configurations
{
    public static InvoiceConfiguration Build(
        InvoiceConfigurationId? id = null,
        string invoiceDescription = "Test Invoice",
        InvoiceFrequency frequency = InvoiceFrequency.Monthly,
        DateOnly? startDate = null,
        bool isActive = true,
        decimal amountTolerance = 0m) =>
        new(
            id ?? new InvoiceConfigurationId("test-config"),
            IntegrationType.Microsoft365,
            invoiceDescription,
            frequency,
            new Money(10.00m, "GBP"),
            VatMode.Exclusive,
            IsActive: isActive,
            OneDriveDestination: "/drives/test/root:/Bills/Test",
            StartDate: startDate ?? new DateOnly(2025, 1, 1),
            BillingAccountId: "test:billing:account",
            DateToleranceDays: 5,
            AmountTolerance: amountTolerance);
}
