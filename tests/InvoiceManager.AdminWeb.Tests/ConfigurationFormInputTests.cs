using InvoiceManager.AdminWeb.Pages.Configurations;
using InvoiceManager.Core;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;

namespace InvoiceManager.AdminWeb.Tests;

public sealed class ConfigurationFormInputTests
{
    private static readonly OneDriveFolder Folder = new("drive-id", "Drive", "folder-id", "/Bills");

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
        };

        Assert.ThrowsAny<ArgumentException>(() => input.Build(
            false,
            [new BillingAccountChoice("billing-id", "Billing account", "Business")],
            currentBillingAccountId: null,
            Folder));
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
        };

        var configuration = input.Build(false, [], currentBillingAccountId: null, Folder);

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
        };

        var configuration = input.Build(
            false, [new BillingAccountChoice("billing-id", "Billing account", "Business")], currentBillingAccountId: null, Folder);

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
        };

        Assert.ThrowsAny<ArgumentException>(() => input.Build(false, [], currentBillingAccountId: null, Folder));
    }

    [Fact]
    public void Build_AcceptsUnchangedBillingAccountMissingFromDiscovery()
    {
        // Simulates an Edit where discovery is temporarily unavailable/incomplete but the
        // account being submitted is the same one already stored server-side.
        var input = new ConfigurationFormInput
        {
            Id = "billing-invoice",
            IntegrationType = IntegrationType.MicrosoftBilling,
            BillingAccountId = "stored-id",
        };

        var configuration = input.Build(false, [], currentBillingAccountId: "stored-id", Folder);

        var billing = Assert.IsType<MicrosoftBillingIntegrationConfiguration>(configuration.IntegrationConfiguration.Value);
        Assert.Equal("stored-id", billing.BillingAccountId);
    }

    [Fact]
    public void Build_RejectsForgedBillingAccount_EvenWhenPostedOriginalMatches()
    {
        // A forged request that sets both BillingAccountId and OriginalBillingAccountId to the
        // same arbitrary value must still be rejected: currentBillingAccountId is supplied by
        // the caller from server-loaded state, not from Input.OriginalBillingAccountId, so this
        // no longer has any effect on the outcome.
        var input = new ConfigurationFormInput
        {
            Id = "billing-invoice",
            IntegrationType = IntegrationType.MicrosoftBilling,
            BillingAccountId = "forged-id",
            OriginalBillingAccountId = "forged-id",
        };

        Assert.ThrowsAny<ArgumentException>(() => input.Build(false, [], currentBillingAccountId: "real-stored-id", Folder));
    }
}
