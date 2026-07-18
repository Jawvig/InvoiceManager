using InvoiceManager.Core;
using InvoiceManager.Infrastructure.CosmosDb;
using NodaMoney;

namespace InvoiceManager.Infrastructure.Tests;

public sealed class InvoiceConfigurationDocumentTests
{
    [Fact]
    public void FromConfiguration_PersistsCompleteAmountMatchingCriteria()
    {
        var configuration = new InvoiceConfiguration(
            new InvoiceConfigurationId("azure-test"), IntegrationType.Microsoft365, "Test",
            InvoiceFrequency.Monthly,
            new AmountMatchingCriteria(new Money(10m, "GBP"), 0.25m), VatMode.Inclusive,
            true, "/drives/test/root:/Bills", new DateOnly(2026, 1, 1), "account", 5);

        var document = InvoiceConfigurationDocument.FromConfiguration(configuration);
        var roundTripped = document.ToConfiguration();

        Assert.Equal(configuration.AmountMatchingCriteria, roundTripped.AmountMatchingCriteria);
    }

    [Fact]
    public void FromConfiguration_LeavesAmountMatchingCriteriaAbsent()
    {
        var configuration = new InvoiceConfiguration(
            new InvoiceConfigurationId("azure-test"), IntegrationType.Azure, "",
            InvoiceFrequency.Monthly, Option.None, VatMode.Inclusive,
            true, "/drives/test/root:/Bills", new DateOnly(2026, 1, 1), "account", 5);

        var document = InvoiceConfigurationDocument.FromConfiguration(configuration);

        Assert.True(document.AmountMatchingCriteria is null);
        Assert.True(document.ToConfiguration().AmountMatchingCriteria is None);
    }

    [Fact]
    public void FromConfiguration_RoundTripsStableOneDriveDestination()
    {
        var configuration = new InvoiceConfiguration(
            new("stable-folder"), IntegrationType.Azure, "Azure",
            InvoiceFrequency.Monthly, Option.None, VatMode.Inclusive, true,
            new OneDriveDestination("/Bills/Azure", "drive-id", "folder-id"),
            new DateOnly(2026, 1, 1), "account", 5);

        var roundTripped = InvoiceConfigurationDocument.FromConfiguration(configuration).ToConfiguration();

        Assert.Equal(configuration.OneDriveDestination, roundTripped.OneDriveDestination);
        Assert.Equal("/drives/drive-id/items/folder-id", roundTripped.OneDriveDestination.GraphPath);
    }

    [Fact]
    public void FromConfiguration_RoundTripsEmailMatchingFields()
    {
        var configuration = new InvoiceConfiguration(
            new InvoiceConfigurationId("email-test"), IntegrationType.Microsoft365Email, "Test",
            InvoiceFrequency.Monthly, Option.None, VatMode.Inclusive,
            true, "/drives/test/root:/Bills", new DateOnly(2026, 1, 1), "", 5,
            SenderEmailAddress: "billing@contoso.com",
            BodyPattern: "Invoice for account \\d+");

        var document = InvoiceConfigurationDocument.FromConfiguration(configuration);
        var roundTripped = document.ToConfiguration();

        Assert.Equal("billing@contoso.com", document.SenderEmailAddress);
        Assert.Equal("Invoice for account \\d+", document.BodyPattern);
        Assert.Equal(configuration.SenderEmailAddress, roundTripped.SenderEmailAddress);
        Assert.Equal(configuration.BodyPattern, roundTripped.BodyPattern);
    }

    [Fact]
    public void ToConfiguration_DefaultsEmailMatchingFields_WhenAbsentFromJson()
    {
        var json = """
            {
              "id": "azure-test",
              "integrationType": "Azure",
              "invoiceDescription": "",
              "frequency": "Monthly",
              "defaultVatMode": "Inclusive",
              "isActive": true,
              "oneDriveDestination": "/drives/test/root:/Bills",
              "startDate": "2026-01-01",
              "billingAccountId": "account",
              "dateToleranceDays": 5
            }
            """;

        var document = System.Text.Json.JsonSerializer.Deserialize<InvoiceConfigurationDocument>(json)!;
        var configuration = document.ToConfiguration();

        Assert.Equal("", configuration.SenderEmailAddress);
        Assert.Equal("", configuration.BodyPattern);
    }
}
