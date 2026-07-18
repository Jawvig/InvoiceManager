using InvoiceManager.Core;

namespace InvoiceManager.Core.Tests;

public sealed class InvoiceConfigurationValidationTests
{
    [Theory]
    [InlineData("Microsoft 365", "microsoft-365")]
    [InlineData("Crème brûlée", "creme-brulee")]
    [InlineData("", "microsoft365-invoice")]
    public void GenerateSlug_ProducesEditableLowercaseKebabCase(string description, string expected) =>
        Assert.Equal(expected, InvoiceConfigurationValidation.GenerateSlug(description, IntegrationType.Microsoft365));

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
}
