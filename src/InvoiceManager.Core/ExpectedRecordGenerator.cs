using InvoiceManager.Core.Repositories;

namespace InvoiceManager.Core;

/// <summary>
/// Generates the next expected invoice record for a configuration.
/// Reusable by both the timer trigger on startup and the post-processing step
/// after a successful invoice save.
/// </summary>
public sealed class ExpectedRecordGenerator(IInvoiceRecordRepository repository)
{
    public async Task GenerateAsync(InvoiceConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var mostRecent = await repository.GetMostRecentAsync(configuration.Id, cancellationToken);
        var nextDateResult = NextExpectedInvoiceDate.CalculateNext(configuration, mostRecent);

        if (nextDateResult is not NextExpectedDate nextExpectedDate)
            return;

        var nextDate = nextExpectedDate.Date;

        if (await repository.ExistsAsync(configuration.Id, nextDate, cancellationToken))
            return;

        var record = new InvoiceRecord(
            InvoiceRecordId.NewId(),
            configuration.Id,
            configuration.InvoiceDescription,
            nextDate,
            configuration.DateToleranceDays,
            configuration.DefaultExpectedAmount,
            configuration.DefaultVatMode,
            ProcessingStatus.Expected,
            Option.None);

        await repository.CreateAsync(record, cancellationToken);
    }
}
