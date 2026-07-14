using InvoiceManager.Core;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace InvoiceManager.Functions.Functions;

public sealed class GenerateExpectedRecordsTimer(
    ExpectedRecordGenerator generator,
    DueInvoiceProcessor processor,
    ILogger<GenerateExpectedRecordsTimer> logger)
{
    [Function("GenerateExpectedRecordsTimer")]
    public async Task RunAsync([TimerTrigger("0 0 * * * *")] TimerInfo timerInfo, CancellationToken cancellationToken)
    {
        logger.LogInformation("Expected record generation triggered by timer.");
        var generationResults = await generator.GenerateForAllActiveAsync(cancellationToken);
        logger.LogInformation(
            "Expected record generation complete: {SucceededCount} succeeded, {FailedCount} failed.",
            generationResults.Count(r => r is GenerationSucceeded),
            generationResults.Count(r => r is GenerationFailed));

        var processingResults = await processor.ProcessDueAsync(cancellationToken);
        logger.LogInformation(
            "Due invoice processing complete: {SavedCount} saved, {ReconciledCount} reconciled, " +
            "{NoMatchCount} no match yet, {NotFoundCount} not found, {FailedCount} failed.",
            processingResults.Count(r => r is ProcessingSucceeded),
            processingResults.Count(r => r is ProcessingReconciled),
            processingResults.Count(r => r is ProcessingNoMatch),
            processingResults.Count(r => r is ProcessingNotFound),
            processingResults.Count(r => r is ProcessingFailed));
    }
}
