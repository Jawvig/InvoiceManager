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
}
