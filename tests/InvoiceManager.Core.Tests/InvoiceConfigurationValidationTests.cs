using InvoiceManager.Core;

namespace InvoiceManager.Core.Tests;

public sealed class InvoiceConfigurationValidationTests
{
    [Theory]
    [InlineData("Microsoft 365", "microsoft-365")]
    [InlineData("Crème brûlée", "creme-brulee")]
    [InlineData("", "microsoftbilling-invoice")]
    public void GenerateSlug_ProducesEditableLowercaseKebabCase(string description, string expected) =>
        Assert.Equal(expected, InvoiceConfigurationValidation.GenerateSlug(description, IntegrationType.MicrosoftBilling));

    [Fact]
    public void Validate_RejectsInvalidAmountsAndDateTolerance()
    {
        var configuration = InvoiceManager.TestSupport.Configurations.Build(amountTolerance: -1m) with
        {
            DateToleranceDays = 366,
            AmountMatchingCriteria = new AmountMatchingCriteria(new NodaMoney.Money(0m, "GBP"), -1m),
        };

        var errors = InvoiceConfigurationValidation.Validate(configuration);

        Assert.Contains(errors, x => x.Contains("greater than zero"));
        Assert.Contains(errors, x => x.Contains("non-negative"));
        Assert.Contains(errors, x => x.Contains("365"));
    }

    [Theory]
    [InlineData("Uppercase")]
    [InlineData("has spaces")]
    [InlineData("leading-")]
    [InlineData(" ")]
    public void Validate_RejectsMalformedIds(string id)
    {
        var configuration = InvoiceManager.TestSupport.Configurations.Build() with { Id = new(id) };

        Assert.Contains(
            InvoiceConfigurationValidation.Validate(configuration),
            error => error.Contains("lowercase kebab-case"));
    }

    [Fact]
    public void Validate_RejectsMicrosoftBillingConfigurationWithoutBillingAccountId()
    {
        var configuration = InvoiceManager.TestSupport.Configurations.Build(
            integrationConfiguration: new MicrosoftBillingIntegrationConfiguration(""));

        Assert.Contains(
            InvoiceConfigurationValidation.Validate(configuration),
            error => error.Contains("Billing account is required"));
    }

    [Theory]
    [InlineData("", "pattern", "Sender email address is required")]
    [InlineData("not-an-email", "pattern", "Sender email address must be valid")]
    [InlineData("sender@example.com", "", "Email body pattern is required")]
    [InlineData("sender@example.com", "(unterminated", "Email body pattern must be a valid regular expression")]
    public void Validate_RejectsMalformedGraphEmailConfiguration(string senderEmailAddress, string bodyPattern, string expectedError)
    {
        var configuration = InvoiceManager.TestSupport.Configurations.Build(
            integrationConfiguration: new GraphEmailIntegrationConfiguration(senderEmailAddress, bodyPattern));

        Assert.Contains(
            InvoiceConfigurationValidation.Validate(configuration),
            error => error.Contains(expectedError));
    }

    [Fact]
    public void Validate_AcceptsValidGraphEmailConfiguration()
    {
        var configuration = InvoiceManager.TestSupport.Configurations.Build(
            integrationConfiguration: new GraphEmailIntegrationConfiguration("sender@example.com", "Invoice.*"));

        Assert.DoesNotContain(
            InvoiceConfigurationValidation.Validate(configuration),
            error => error.Contains("Sender email") || error.Contains("body pattern"));
    }

    [Theory]
    [InlineData("", "drive", "folder-item", "path", "OneDrive drive ID is required.")]
    [InlineData("drive", "", "folder-item", "path", "OneDrive drive name is required.")]
    [InlineData("drive", "drive-name", "", "path", "OneDrive folder item ID is required.")]
    [InlineData("drive", "drive-name", "folder-item", "", "OneDrive folder path is required.")]
    public void Validate_RejectsIncompleteOneDriveFolder(
        string driveId, string driveName, string folderItemId, string folderPath, string expectedError)
    {
        var configuration = InvoiceManager.TestSupport.Configurations.Build(
            oneDriveFolder: new OneDriveFolder(driveId, driveName, folderItemId, folderPath));

        Assert.Contains(InvoiceConfigurationValidation.Validate(configuration), error => error == expectedError);
    }

    [Fact]
    public void ValidateNoDuplicateMatch_RejectsSameBillingAccountIdAndAmount_EvenWithDifferentFolder()
    {
        var candidate = InvoiceManager.TestSupport.Configurations.Build(
            id: new("new-config"),
            integrationConfiguration: new MicrosoftBillingIntegrationConfiguration("account-1"),
            oneDriveFolder: new OneDriveFolder("drive-2", "Drive Two", "folder-2", "/Bills/Other"));
        var other = InvoiceManager.TestSupport.Configurations.Build(
            id: new("existing-config"),
            integrationConfiguration: new MicrosoftBillingIntegrationConfiguration("account-1"));

        var conflict = InvoiceConfigurationValidation.ValidateNoDuplicateMatch(candidate, [other]);

        Assert.True(conflict is InvoiceConfigurationId id && id.Value == "existing-config");
    }

    [Fact]
    public void ValidateNoDuplicateMatch_AllowsSameBillingAccountId_WhenExpectedAmountRangesDoNotOverlap()
    {
        // Mirrors the seeded m365-business-basic/m365-copilot pair: one Microsoft 365 billing
        // account routinely bills more than one distinct product, distinguished only by expected
        // amount - billing account ID alone must not be treated as the whole match key.
        var businessBasic = InvoiceManager.TestSupport.Configurations.Build(
            id: new("m365-business-basic"),
            integrationConfiguration: new MicrosoftBillingIntegrationConfiguration("account-1")) with
        {
            AmountMatchingCriteria = new AmountMatchingCriteria(new NodaMoney.Money(11.59m, "GBP"), 0m),
        };
        var copilot = InvoiceManager.TestSupport.Configurations.Build(
            id: new("m365-copilot"),
            integrationConfiguration: new MicrosoftBillingIntegrationConfiguration("account-1")) with
        {
            AmountMatchingCriteria = new AmountMatchingCriteria(new NodaMoney.Money(29.11m, "GBP"), 0m),
        };

        Assert.True(InvoiceConfigurationValidation.ValidateNoDuplicateMatch(copilot, [businessBasic]) is None);
    }

    [Fact]
    public void ValidateNoDuplicateMatch_RejectsOverlappingAmountRanges_EvenWhenNotEqual()
    {
        // £100 ± £10 accepts £90-£110; £105 ± £10 accepts £95-£115 - these overlap (£95-£110) even
        // though the expected amounts differ, so either configuration could match an invoice the
        // other was meant to. Equality was the old (too strict, and also too permissive here) rule.
        var candidate = InvoiceManager.TestSupport.Configurations.Build(
            id: new("new-config"),
            integrationConfiguration: new MicrosoftBillingIntegrationConfiguration("account-1")) with
        {
            AmountMatchingCriteria = new AmountMatchingCriteria(new NodaMoney.Money(105m, "GBP"), 10m),
        };
        var other = InvoiceManager.TestSupport.Configurations.Build(
            id: new("existing-config"),
            integrationConfiguration: new MicrosoftBillingIntegrationConfiguration("account-1")) with
        {
            AmountMatchingCriteria = new AmountMatchingCriteria(new NodaMoney.Money(100m, "GBP"), 10m),
        };

        var conflict = InvoiceConfigurationValidation.ValidateNoDuplicateMatch(candidate, [other]);

        Assert.True(conflict is InvoiceConfigurationId id && id.Value == "existing-config");
    }

    [Fact]
    public void ValidateNoDuplicateMatch_AllowsNonOverlappingAmountRanges()
    {
        var candidate = InvoiceManager.TestSupport.Configurations.Build(
            id: new("new-config"),
            integrationConfiguration: new MicrosoftBillingIntegrationConfiguration("account-1")) with
        {
            AmountMatchingCriteria = new AmountMatchingCriteria(new NodaMoney.Money(105m, "GBP"), 1m),
        };
        var other = InvoiceManager.TestSupport.Configurations.Build(
            id: new("existing-config"),
            integrationConfiguration: new MicrosoftBillingIntegrationConfiguration("account-1")) with
        {
            AmountMatchingCriteria = new AmountMatchingCriteria(new NodaMoney.Money(100m, "GBP"), 1m),
        };

        Assert.True(InvoiceConfigurationValidation.ValidateNoDuplicateMatch(candidate, [other]) is None);
    }

    [Fact]
    public void ValidateNoDuplicateMatch_RejectsSameBillingAccountId_WhenEitherSideHasNoAmountCriteria()
    {
        // A configuration with no amount criteria accepts every amount, so it would compete for
        // whatever the other configuration matches too - this must be rejected even though the
        // amounts themselves were never compared as "equal".
        var unrestricted = InvoiceManager.TestSupport.Configurations.Build(
            id: new("new-config"),
            integrationConfiguration: new MicrosoftBillingIntegrationConfiguration("account-1")) with
        {
            AmountMatchingCriteria = Option.None,
        };
        var other = InvoiceManager.TestSupport.Configurations.Build(
            id: new("existing-config"),
            integrationConfiguration: new MicrosoftBillingIntegrationConfiguration("account-1")) with
        {
            AmountMatchingCriteria = new AmountMatchingCriteria(new NodaMoney.Money(29.11m, "GBP"), 0m),
        };

        var conflict = InvoiceConfigurationValidation.ValidateNoDuplicateMatch(unrestricted, [other]);

        Assert.True(conflict is InvoiceConfigurationId id && id.Value == "existing-config");
    }

    [Fact]
    public void ValidateNoDuplicateMatch_RejectsSameBillingAccountId_WhenNeitherHasAmountCriteria()
    {
        var candidate = InvoiceManager.TestSupport.Configurations.Build(
            id: new("new-config"),
            integrationConfiguration: new MicrosoftBillingIntegrationConfiguration("account-1")) with
        {
            AmountMatchingCriteria = Option.None,
        };
        var other = InvoiceManager.TestSupport.Configurations.Build(
            id: new("existing-config"),
            integrationConfiguration: new MicrosoftBillingIntegrationConfiguration("account-1")) with
        {
            AmountMatchingCriteria = Option.None,
        };

        var conflict = InvoiceConfigurationValidation.ValidateNoDuplicateMatch(candidate, [other]);

        Assert.True(conflict is InvoiceConfigurationId id && id.Value == "existing-config");
    }

    [Fact]
    public void ValidateNoDuplicateMatch_RejectsSameSenderAndBodyPattern_ButNotDifferentPattern()
    {
        var candidate = InvoiceManager.TestSupport.Configurations.Build(
            id: new("new-config"),
            integrationConfiguration: new GraphEmailIntegrationConfiguration("sender@example.com", "Invoice \\d+"));
        var sameCriteria = InvoiceManager.TestSupport.Configurations.Build(
            id: new("existing-config"),
            integrationConfiguration: new GraphEmailIntegrationConfiguration("sender@example.com", "Invoice \\d+"));
        var differentPattern = InvoiceManager.TestSupport.Configurations.Build(
            id: new("other-config"),
            integrationConfiguration: new GraphEmailIntegrationConfiguration("sender@example.com", "Statement \\d+"));

        var conflict = InvoiceConfigurationValidation.ValidateNoDuplicateMatch(candidate, [sameCriteria]);
        Assert.True(conflict is InvoiceConfigurationId id && id.Value == "existing-config");
        Assert.True(InvoiceConfigurationValidation.ValidateNoDuplicateMatch(candidate, [differentPattern]) is None);
    }

    [Fact]
    public void ValidateNoDuplicateMatch_IgnoresConfigurationsOfADifferentIntegrationType()
    {
        var candidate = InvoiceManager.TestSupport.Configurations.Build(
            id: new("new-config"),
            integrationConfiguration: new MicrosoftBillingIntegrationConfiguration("account-1"));
        var differentType = InvoiceManager.TestSupport.Configurations.Build(
            id: new("existing-config"),
            integrationConfiguration: new GraphEmailIntegrationConfiguration("sender@example.com", "account-1"));

        Assert.True(InvoiceConfigurationValidation.ValidateNoDuplicateMatch(candidate, [differentType]) is None);
    }

    [Fact]
    public void ValidateNoDuplicateMatch_IgnoresItself()
    {
        var candidate = InvoiceManager.TestSupport.Configurations.Build(
            id: new("existing-config"),
            integrationConfiguration: new MicrosoftBillingIntegrationConfiguration("account-1"));

        Assert.True(InvoiceConfigurationValidation.ValidateNoDuplicateMatch(candidate, [candidate]) is None);
    }
}
