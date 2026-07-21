using InvoiceManager.AdminWeb.Pages.Configurations;
using InvoiceManager.Core;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;

namespace InvoiceManager.AdminWeb.Tests;

public sealed class ConfigurationFormInputTests
{
    [Fact]
    public void Build_RejectsInvalidCurrencyCode()
    {
        var input = new ConfigurationFormInput
        {
            Id = "test-invoice",
            HasExpectedAmount = true,
            ExpectedAmount = 10m,
            Currency = "NOT-A-CURRENCY",
            BillingAccountId = "billing-id",
            DriveId = "drive-id",
            DriveName = "Drive",
            FolderItemId = "folder-id",
            FolderPath = "/Bills",
        };

        Assert.ThrowsAny<ArgumentException>(() => input.Build(
            false,
            [new BillingAccountChoice("billing-id", "Billing account", "Business")],
            false));
    }

    [Fact]
    public void Build_SupportsGraphEmailWithoutBillingAccount()
    {
        var input = new ConfigurationFormInput
        {
            Id = "email-invoice",
            IntegrationType = IntegrationType.GraphEmail,
            SenderEmailAddress = "billing@example.com",
            BodyPattern = "Invoice \\d+",
            DriveId = "drive-id",
            DriveName = "Drive",
            FolderItemId = "folder-id",
            FolderPath = "/Bills",
        };

        var configuration = input.Build(false, [], false);

        var email = Assert.IsType<GraphEmailIntegrationConfiguration>(configuration.IntegrationConfiguration.Value);
        Assert.Equal("billing@example.com", email.SenderEmailAddress);
        Assert.Equal("Invoice \\d+", email.BodyPattern);
        Assert.Equal(IntegrationType.GraphEmail, configuration.IntegrationType);
    }

    [Fact]
    public void Build_SupportsMicrosoftBillingWithSelectedAccount()
    {
        var input = new ConfigurationFormInput
        {
            Id = "billing-invoice",
            IntegrationType = IntegrationType.MicrosoftBilling,
            BillingAccountId = "billing-id",
            DriveId = "drive-id",
            DriveName = "Drive",
            FolderItemId = "folder-id",
            FolderPath = "/Bills",
        };

        var configuration = input.Build(
            false, [new BillingAccountChoice("billing-id", "Billing account", "Business")], false);

        var billing = Assert.IsType<MicrosoftBillingIntegrationConfiguration>(configuration.IntegrationConfiguration.Value);
        Assert.Equal("billing-id", billing.BillingAccountId);
        Assert.Equal(IntegrationType.MicrosoftBilling, configuration.IntegrationType);
        Assert.Equal("drive-id", configuration.OneDriveFolder.DriveId);
        Assert.Equal("folder-id", configuration.OneDriveFolder.FolderItemId);
        Assert.Equal("/Bills", configuration.OneDriveFolder.FolderPath);
    }

    [Fact]
    public void Build_RejectsBillingAccountNotReturnedByDiscovery()
    {
        var input = new ConfigurationFormInput
        {
            Id = "billing-invoice",
            IntegrationType = IntegrationType.MicrosoftBilling,
            BillingAccountId = "unknown-id",
            DriveId = "drive-id",
            DriveName = "Drive",
            FolderItemId = "folder-id",
            FolderPath = "/Bills",
        };

        Assert.ThrowsAny<ArgumentException>(() => input.Build(false, [], false));
    }
}
