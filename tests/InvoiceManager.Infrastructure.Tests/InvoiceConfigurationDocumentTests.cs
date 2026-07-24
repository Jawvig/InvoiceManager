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
            new InvoiceConfigurationId("azure-test"),
            new MicrosoftBillingIntegrationConfiguration("account"), "Test",
            InvoiceFrequency.Monthly,
            new AmountMatchingCriteria(new Money(10m, "GBP"), 0.25m), VatMode.Inclusive,
            true, new OneDriveFolder("drive-id", "Drive", "folder-id", "/Bills"), new DateOnly(2026, 1, 1), 5);

        var document = InvoiceConfigurationDocument.FromConfiguration(configuration);
        var roundTripped = document.ToConfiguration();

        Assert.Equal(configuration.AmountMatchingCriteria, roundTripped.AmountMatchingCriteria);
    }

    [Fact]
    public void FromConfiguration_LeavesAmountMatchingCriteriaAbsent()
    {
        var configuration = new InvoiceConfiguration(
            new InvoiceConfigurationId("azure-test"),
            new MicrosoftBillingIntegrationConfiguration("account"), "",
            InvoiceFrequency.Monthly, Option.None, VatMode.Inclusive,
            true, new OneDriveFolder("drive-id", "Drive", "folder-id", "/Bills"), new DateOnly(2026, 1, 1), 5);

        var document = InvoiceConfigurationDocument.FromConfiguration(configuration);

        Assert.True(document.AmountMatchingCriteria is null);
        Assert.True(document.ToConfiguration().AmountMatchingCriteria is None);
    }

    [Fact]
    public void FromConfiguration_RoundTripsStableOneDriveFolder()
    {
        var configuration = new InvoiceConfiguration(
            new("stable-folder"),
            new MicrosoftBillingIntegrationConfiguration("account"), "Azure",
            InvoiceFrequency.Monthly, Option.None, VatMode.Inclusive, true,
            new OneDriveFolder("drive-id", "Drive", "folder-id", "/Bills/Azure"),
            new DateOnly(2026, 1, 1), 5);

        var roundTripped = InvoiceConfigurationDocument.FromConfiguration(configuration).ToConfiguration();

        Assert.Equal(configuration.OneDriveFolder, roundTripped.OneDriveFolder);
        Assert.Equal("/drives/drive-id/items/folder-id", roundTripped.OneDriveFolder.GraphPath);
    }

    [Fact]
    public void FromConfiguration_RoundTripsGraphEmailIntegrationConfiguration()
    {
        var configuration = new InvoiceConfiguration(
            new InvoiceConfigurationId("email-test"),
            new GraphEmailIntegrationConfiguration("billing@contoso.com", "Invoice for account \\d+"), "Test",
            InvoiceFrequency.Monthly, Option.None, VatMode.Inclusive,
            true, new OneDriveFolder("drive-id", "Drive", "folder-id", "/Bills"), new DateOnly(2026, 1, 1), 5);

        var document = InvoiceConfigurationDocument.FromConfiguration(configuration);
        var roundTripped = document.ToConfiguration();

        Assert.Equal("graphEmail", document.IntegrationConfiguration.Type);
        Assert.Equal("billing@contoso.com", document.IntegrationConfiguration.SenderEmailAddress);
        Assert.Equal("Invoice for account \\d+", document.IntegrationConfiguration.BodyPattern);
        Assert.True(roundTripped.IntegrationConfiguration is GraphEmailIntegrationConfiguration);
        Assert.Equal(configuration.IntegrationConfiguration, roundTripped.IntegrationConfiguration);
        Assert.Equal(IntegrationType.GraphEmail, roundTripped.IntegrationType);
    }

    [Fact]
    public void FromConfiguration_RoundTripsMicrosoftBillingIntegrationConfiguration()
    {
        var configuration = new InvoiceConfiguration(
            new InvoiceConfigurationId("billing-test"),
            new MicrosoftBillingIntegrationConfiguration("account-123"), "Test",
            InvoiceFrequency.Monthly, Option.None, VatMode.Inclusive,
            true, new OneDriveFolder("drive-id", "Drive", "folder-id", "/Bills"), new DateOnly(2026, 1, 1), 5);

        var document = InvoiceConfigurationDocument.FromConfiguration(configuration);
        var roundTripped = document.ToConfiguration();

        Assert.Equal("microsoftBilling", document.IntegrationConfiguration.Type);
        Assert.Equal("account-123", document.IntegrationConfiguration.BillingAccountId);
        Assert.Equal(configuration.IntegrationConfiguration, roundTripped.IntegrationConfiguration);
        Assert.Equal(IntegrationType.MicrosoftBilling, roundTripped.IntegrationType);
    }

    [Fact]
    public void ToConfiguration_IgnoresStoredIntegrationTypeField_DerivingFromIntegrationConfiguration()
    {
        var json = """
            {
              "id": "billing-test",
              "integrationType": "graphEmail",
              "integrationConfiguration": { "type": "microsoftBilling", "billingAccountId": "account" },
              "invoiceDescription": "",
              "frequency": "Monthly",
              "defaultVatMode": "Inclusive",
              "isActive": true,
              "oneDriveFolder": { "driveId": "d", "driveName": "Drive", "folderItemId": "f", "folderPath": "/Bills" },
              "startDate": "2026-01-01",
              "dateToleranceDays": 5
            }
            """;

        var document = System.Text.Json.JsonSerializer.Deserialize<InvoiceConfigurationDocument>(json)!;
        var configuration = document.ToConfiguration();

        Assert.Equal(IntegrationType.MicrosoftBilling, configuration.IntegrationType);
    }
}
