using InvoiceManager.Core;

namespace InvoiceManager.Core.Tests;

public sealed class NextExpectedInvoiceDateTests
{
    [Fact]
    public void CalculateNext_ReturnsStartDate_WhenNoRecordsExist()
    {
        var config = new InvoiceConfiguration(
            new DateOnly(2025, 7, 10),
            InvoiceFrequency.Monthly);

        var result = NextExpectedInvoiceDate.CalculateNext(config, new None());

        Assert.Equal(new DateOnly(2025, 7, 10), ExpectedDate(result));
    }

    [Fact]
    public void CalculateNext_ReturnsActualDatePlusFrequency_WhenMostRecentRecordIsSaved()
    {
        var config = new InvoiceConfiguration(
            new DateOnly(2025, 7, 10),
            InvoiceFrequency.Monthly);
        var mostRecent = new InvoiceRecord(
            ProcessingStatus.SavedToOneDrive,
            new DateOnly(2026, 6, 10));

        var result = NextExpectedInvoiceDate.CalculateNext(config, mostRecent);

        Assert.Equal(new DateOnly(2026, 7, 10), ExpectedDate(result));
    }

    [Fact]
    public void CalculateNext_ReturnsInProgress_WhenMostRecentRecordIsBeforeSaved()
    {
        var config = new InvoiceConfiguration(
            new DateOnly(2025, 7, 10),
            InvoiceFrequency.Monthly);
        var mostRecent = new InvoiceRecord(ProcessingStatus.Expected);

        var result = NextExpectedInvoiceDate.CalculateNext(config, mostRecent);

        Assert.True(IsInProgress(result));
    }

    [Fact]
    public void CalculateNext_ReturnsActualDatePlusFrequency_WhenMostRecentRecordIsReconciled()
    {
        var config = new InvoiceConfiguration(
            new DateOnly(2025, 7, 10),
            InvoiceFrequency.Monthly);
        var mostRecent = new InvoiceRecord(
            ProcessingStatus.ReconciledFromOneDrive,
            new DateOnly(2026, 6, 10));

        var result = NextExpectedInvoiceDate.CalculateNext(config, mostRecent);

        Assert.Equal(new DateOnly(2026, 7, 10), ExpectedDate(result));
    }

    private static DateOnly ExpectedDate(NextExpectedDateResult result) => result switch
    {
        NextExpectedDate next => next.Date,
        InvoiceInProgress => throw new Xunit.Sdk.XunitException(
            "Expected NextExpectedDate but got InvoiceInProgress."),
    };

    private static bool IsInProgress(NextExpectedDateResult result) => result switch
    {
        InvoiceInProgress => true,
        NextExpectedDate => false,
    };
}
