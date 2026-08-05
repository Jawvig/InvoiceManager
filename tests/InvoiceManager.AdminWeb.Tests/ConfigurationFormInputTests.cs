using InvoiceManager.AdminWeb.Pages.Configurations;
using InvoiceManager.Core;
using InvoiceManager.Core.Integrations.FreeAgent;
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

    [Fact]
    public void Build_OmitsFreeAgentMatching_WhenNotEnabled()
    {
        var input = new ConfigurationFormInput
        {
            Id = "billing-invoice",
            IntegrationType = IntegrationType.MicrosoftBilling,
            BillingAccountId = "stored-id",
        };

        var configuration = input.Build(false, [], currentBillingAccountId: "stored-id", Folder);

        Assert.True(configuration.FreeAgentMatching is None, "Expected FreeAgentMatching to be absent.");
    }

    [Fact]
    public void Build_SetsFreeAgentMatching_WhenEnabled()
    {
        var input = new ConfigurationFormInput
        {
            Id = "billing-invoice",
            IntegrationType = IntegrationType.MicrosoftBilling,
            BillingAccountId = "stored-id",
            HasFreeAgentMatching = true,
            FreeAgentContactUrl = "https://api.sandbox.freeagent.com/v2/contacts/1",
            FreeAgentContactDisplayName = "Example Contact",
            HasFreeAgentDateReconciliation = true,
            FreeAgentDateToleranceDays = 3,
            HasFreeAgentAmountReconciliation = true,
            FreeAgentAmountTolerance = 0.01m,
        };

        var configuration = input.Build(false, [], currentBillingAccountId: "stored-id", Folder);

        var matching = Assert.IsType<FreeAgentBillMatching>(configuration.FreeAgentMatching.Value);
        Assert.Equal("https://api.sandbox.freeagent.com/v2/contacts/1", matching.Contact.Url.Url.OriginalString);
        Assert.Equal("Example Contact", matching.Contact.DisplayName);
        var dateReconciliation = Assert.IsType<FreeAgentDateReconciliation>(matching.DateReconciliation.Value);
        Assert.Equal(3, dateReconciliation.ToleranceDays);
        var amountReconciliation = Assert.IsType<FreeAgentAmountReconciliation>(matching.AmountReconciliation.Value);
        Assert.Equal(0.01m, amountReconciliation.AmountTolerance);
    }

    [Fact]
    public void Build_OmitsFreeAgentReconciliation_WhenNotEnabled_EvenWithLeftoverToleranceValues()
    {
        // A checkbox that was checked-then-unchecked can leave a stray tolerance value posted
        // alongside it - the unchecked box must still translate to Option.None, not a
        // zero-or-leftover tolerance, or the invalid boolean/tolerance pairing the domain type
        // was designed to prevent would just reappear at the form-input layer.
        var input = new ConfigurationFormInput
        {
            Id = "billing-invoice",
            IntegrationType = IntegrationType.MicrosoftBilling,
            BillingAccountId = "stored-id",
            HasFreeAgentMatching = true,
            FreeAgentContactUrl = "https://api.sandbox.freeagent.com/v2/contacts/1",
            HasFreeAgentDateReconciliation = false,
            FreeAgentDateToleranceDays = 7,
            HasFreeAgentAmountReconciliation = false,
            FreeAgentAmountTolerance = 5.00m,
        };

        var configuration = input.Build(false, [], currentBillingAccountId: "stored-id", Folder);

        var matching = Assert.IsType<FreeAgentBillMatching>(configuration.FreeAgentMatching.Value);
        Assert.True(matching.DateReconciliation is None, "Expected DateReconciliation to be absent.");
        Assert.True(matching.AmountReconciliation is None, "Expected AmountReconciliation to be absent.");
    }

    [Fact]
    public void Build_RejectsInvalidFreeAgentContactUrl()
    {
        var input = new ConfigurationFormInput
        {
            Id = "billing-invoice",
            IntegrationType = IntegrationType.MicrosoftBilling,
            BillingAccountId = "stored-id",
            HasFreeAgentMatching = true,
            FreeAgentContactUrl = "not-a-uri",
        };

        Assert.ThrowsAny<ArgumentException>(() => input.Build(false, [], currentBillingAccountId: "stored-id", Folder));
    }

    [Fact]
    public void Build_RejectsBlankFreeAgentContactUrl_WithoutThrowingNullReferenceException()
    {
        // The model binder can assign null to a string property despite its non-nullable
        // initializer (an absent/blank posted field) - Build() must still report this as an
        // ArgumentException the page handler turns into a form error, not crash with a 500.
        var input = new ConfigurationFormInput
        {
            Id = "billing-invoice",
            IntegrationType = IntegrationType.MicrosoftBilling,
            BillingAccountId = "stored-id",
            HasFreeAgentMatching = true,
            FreeAgentContactUrl = null!,
        };

        Assert.ThrowsAny<ArgumentException>(() => input.Build(false, [], currentBillingAccountId: "stored-id", Folder));
    }

    [Fact]
    public void From_RoundTripsFreeAgentMatching()
    {
        var existing = new FreeAgentBillMatching(
            new FreeAgentContact(new FreeAgentContactIdentity("https://api.sandbox.freeagent.com/v2/contacts/1"), "Example Contact"),
            DateReconciliation: new FreeAgentDateReconciliation(3),
            AmountReconciliation: new FreeAgentAmountReconciliation(0.01m));
        var stored = new StoredInvoiceConfiguration(
            new InvoiceConfiguration(
                new InvoiceConfigurationId("billing-invoice"),
                new MicrosoftBillingIntegrationConfiguration("stored-id"),
                "Description",
                InvoiceFrequency.Monthly,
                Option.None,
                VatMode.Exclusive,
                true,
                Folder,
                DateOnly.FromDateTime(DateTime.UtcNow),
                5,
                existing),
            ETag: "etag");

        var input = ConfigurationFormInput.From(stored);

        Assert.True(input.HasFreeAgentMatching);
        Assert.Equal("https://api.sandbox.freeagent.com/v2/contacts/1", input.FreeAgentContactUrl);
        Assert.Equal("Example Contact", input.FreeAgentContactDisplayName);
        Assert.True(input.HasFreeAgentDateReconciliation);
        Assert.Equal(3, input.FreeAgentDateToleranceDays);
        Assert.True(input.HasFreeAgentAmountReconciliation);
        Assert.Equal(0.01m, input.FreeAgentAmountTolerance);
    }
}
