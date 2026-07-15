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
            new InvoiceConfigurationId("azure-test"), IntegrationType.Microsoft365, "Test",
            InvoiceFrequency.Monthly,
            new AmountMatchingCriteria(new Money(10m, "GBP"), 0.25m), VatMode.Inclusive,
            true, "/drives/test/root:/Bills", new DateOnly(2026, 1, 1), "account", 5);

        var document = InvoiceConfigurationDocument.FromConfiguration(configuration);
        var roundTripped = document.ToConfiguration();

        Assert.Equal(configuration.AmountMatchingCriteria, roundTripped.AmountMatchingCriteria);
    }

    [Fact]
    public void FromConfiguration_LeavesAmountMatchingCriteriaAbsent()
    {
        var configuration = new InvoiceConfiguration(
            new InvoiceConfigurationId("azure-test"), IntegrationType.Azure, "",
            InvoiceFrequency.Monthly, Option.None, VatMode.Inclusive,
            true, "/drives/test/root:/Bills", new DateOnly(2026, 1, 1), "account", 5);

        var document = InvoiceConfigurationDocument.FromConfiguration(configuration);

        Assert.True(document.AmountMatchingCriteria is null);
        Assert.True(document.ToConfiguration().AmountMatchingCriteria is None);
    }
}
