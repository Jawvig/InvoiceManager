using InvoiceManager.Core;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace InvoiceManager.Functions.Functions;

public sealed class GenerateExpectedRecordsTimer(
    ExpectedRecordGenerator generator,
    ILogger<GenerateExpectedRecordsTimer> logger)
{
    [Function("GenerateExpectedRecordsTimer")]
    public async Task RunAsync([TimerTrigger("0 0 * * * *")] TimerInfo timerInfo, CancellationToken cancellationToken)
    {
        logger.LogInformation("Expected record generation triggered by timer.");
        var results = await generator.GenerateForAllActiveAsync(cancellationToken);
        logger.LogInformation(
            "Expected record generation complete: {SucceededCount} succeeded, {FailedCount} failed.",
            results.Count(r => r is GenerationSucceeded),
            results.Count(r => r is GenerationFailed));
    }
}
