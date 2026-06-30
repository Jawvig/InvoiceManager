namespace InvoiceManager.Core;

/// <summary>The next expected invoice date for a configuration.</summary>
public sealed record NextExpectedDate(DateOnly Date);

/// <summary>
/// Indicates the most recent invoice record has not yet reached the success
/// state, so no further expected invoice should be created for now.
/// </summary>
public sealed record InvoiceInProgress;

/// <summary>The outcome of calculating the next expected invoice date.</summary>
public union NextExpectedDateResult(NextExpectedDate, InvoiceInProgress);

/// <summary>
/// Derives the next expected invoice date from a configuration and the most
/// recent invoice record, if any. Today's date is deliberately not consulted.
/// </summary>
public static class NextExpectedInvoiceDate
{
    public static NextExpectedDateResult CalculateNext(
        InvoiceConfiguration configuration,
        Option<InvoiceRecord> mostRecentRecord)
    {
        return mostRecentRecord switch
        {
            None => new NextExpectedDate(configuration.StartDate),
            InvoiceRecord record => NextFromRecord(record, configuration.Frequency),
        };
    }

    private static NextExpectedDateResult NextFromRecord(
        InvoiceRecord record,
        InvoiceFrequency frequency)
    {
        if (!HasReachedSuccessState(record.Status))
        {
            return new InvoiceInProgress();
        }

        return record.ActualInvoiceDate switch
        {
            DateOnly actual => new NextExpectedDate(AddFrequency(actual, frequency)),
            None => throw new InvalidOperationException(
                $"Invoice record in status {record.Status} has no actual invoice date."),
        };
    }

    // A record has reached the success state once its file is in OneDrive, either
    // by being saved there or reconciled against a file already present.
    private static bool HasReachedSuccessState(ProcessingStatus status) => status switch
    {
        ProcessingStatus.SavedToOneDrive => true,
        ProcessingStatus.ReconciledFromOneDrive => true,
        _ => false,
    };

    private static DateOnly AddFrequency(DateOnly date, InvoiceFrequency frequency) =>
        frequency switch
        {
            InvoiceFrequency.Monthly => date.AddMonths(1),
            _ => throw new ArgumentOutOfRangeException(
                nameof(frequency), frequency, "Unsupported invoice frequency."),
        };
}
