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
        decimal amountTolerance = 0m,
        IntegrationConfiguration? integrationConfiguration = null,
        OneDriveFolder? oneDriveFolder = null) =>
        new(
            id ?? new InvoiceConfigurationId("test-config"),
            integrationConfiguration ?? new MicrosoftBillingIntegrationConfiguration("test:billing:account"),
            invoiceDescription,
            frequency,
            new AmountMatchingCriteria(new Money(10.00m, "GBP"), amountTolerance),
            VatMode.Exclusive,
            IsActive: isActive,
            OneDriveFolder: oneDriveFolder ?? new OneDriveFolder("test-drive", "Test Drive", "test-folder-item", "/Bills/Test"),
            StartDate: startDate ?? new DateOnly(2025, 1, 1),
            DateToleranceDays: 5);
}
