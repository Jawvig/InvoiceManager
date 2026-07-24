using InvoiceManager.Core;
using InvoiceManager.Core.Integrations;
using NodaMoney;

namespace InvoiceManager.Core.Tests;

public sealed class InvoiceMatchingTests
{
    [Fact]
    public void MatchesDateAndAmount_WhenCriteriaArePresent()
    {
        var criteria = new InvoiceSearchCriteria(
            new MicrosoftBillingIntegrationConfiguration("account"), new DateOnly(2026, 6, 9), 2,
            new AmountMatchingCriteria(new Money(10m, "GBP"), 0.50m));

        Assert.True(criteria.Matches(new DateOnly(2026, 6, 10), new Money(10.49m, "GBP")));
        Assert.False(criteria.Matches(new DateOnly(2026, 6, 10), new Money(10.51m, "GBP")));
        Assert.False(criteria.Matches(new DateOnly(2026, 6, 10), new Money(10.49m, "USD")));
    }

    [Fact]
    public void MatchesDateOnly_WhenAmountCriteriaAreAbsent()
    {
        var criteria = new InvoiceSearchCriteria(
            new MicrosoftBillingIntegrationConfiguration("account"), new DateOnly(2026, 6, 9), 2, Option.None);

        Assert.True(criteria.Matches(new DateOnly(2026, 6, 11), new Money(999.99m, "USD")));
        Assert.False(criteria.Matches(new DateOnly(2026, 6, 12), new Money(999.99m, "USD")));
    }
}
