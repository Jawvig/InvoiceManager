using InvoiceManager.Core;
using InvoiceManager.Core.Repositories;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace InvoiceManager.Functions.Functions;

public sealed class GenerateExpectedRecordsTimer(
    IInvoiceConfigurationRepository configurationRepository,
    ExpectedRecordGenerator generator,
    ILogger<GenerateExpectedRecordsTimer> logger)
{
    [Function("GenerateExpectedRecordsTimer")]
    public async Task RunAsync([TimerTrigger("0 0 * * * *")] TimerInfo timerInfo, CancellationToken cancellationToken)
    {
        logger.LogInformation("Expected record generation triggered by timer.");
        var configurations = await configurationRepository.ListActiveAsync(cancellationToken);
        foreach (var config in configurations)
            await generator.GenerateAsync(config, cancellationToken);
        logger.LogInformation("Expected record generation complete for {Count} configuration(s).", configurations.Count);
    }
}
