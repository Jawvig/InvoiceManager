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

        var errors = InvoiceConfigurationValidation.ValidateNoDuplicateMatch(candidate, [other]);

        Assert.Contains(errors, e => e.Contains("existing-config"));
    }

    [Fact]
    public void ValidateNoDuplicateMatch_AllowsSameBillingAccountId_WhenExpectedAmountDiffers()
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

        Assert.Empty(InvoiceConfigurationValidation.ValidateNoDuplicateMatch(copilot, [businessBasic]));
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

        Assert.Contains(
            InvoiceConfigurationValidation.ValidateNoDuplicateMatch(candidate, [other]),
            e => e.Contains("existing-config"));
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

        Assert.Contains(
            InvoiceConfigurationValidation.ValidateNoDuplicateMatch(candidate, [sameCriteria]),
            e => e.Contains("existing-config"));
        Assert.Empty(InvoiceConfigurationValidation.ValidateNoDuplicateMatch(candidate, [differentPattern]));
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

        Assert.Empty(InvoiceConfigurationValidation.ValidateNoDuplicateMatch(candidate, [differentType]));
    }

    [Fact]
    public void ValidateNoDuplicateMatch_IgnoresItself()
    {
        var candidate = InvoiceManager.TestSupport.Configurations.Build(
            id: new("existing-config"),
            integrationConfiguration: new MicrosoftBillingIntegrationConfiguration("account-1"));

        Assert.Empty(InvoiceConfigurationValidation.ValidateNoDuplicateMatch(candidate, [candidate]));
    }
}
