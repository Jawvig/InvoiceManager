using InvoiceManager.AdminWeb.Pages.Configurations;
using InvoiceManager.Core;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;

namespace InvoiceManager.AdminWeb.Tests;

public sealed class ConfigurationFormInputTests
{
    [Fact]
    public void Build_RejectsInvalidCurrencyCode()
    {
        var destination = new OneDriveDestination("/Bills", "drive-id", "folder-id");
        var input = new ConfigurationFormInput
        {
            Id = "test-invoice",
            HasExpectedAmount = true,
            ExpectedAmount = 10m,
            Currency = "NOT-A-CURRENCY",
            BillingAccountId = "billing-id",
            FolderItemId = "folder-id",
        };

        Assert.ThrowsAny<ArgumentException>(() => input.Build(
            false,
            [new BillingAccountChoice("billing-id", "Billing account", "Business")],
            [new OneDriveFolderChoice(destination, "Bills")],
            false));
    }

    [Fact]
    public void Build_SupportsMicrosoft365EmailWithoutBillingAccount()
    {
        var destination = new OneDriveDestination("/Bills", "drive-id", "folder-id");
        var input = new ConfigurationFormInput
        {
            Id = "email-invoice",
            IntegrationType = IntegrationType.Microsoft365Email,
            SenderEmailAddress = "billing@example.com",
            BodyPattern = "Invoice \\d+",
            FolderItemId = "folder-id",
        };

        var configuration = input.Build(
            false, [], [new OneDriveFolderChoice(destination, "Bills")], false);

        Assert.Equal("", configuration.BillingAccountId);
        Assert.Equal("billing@example.com", configuration.SenderEmailAddress);
        Assert.Equal("Invoice \\d+", configuration.BodyPattern);
    }
}
