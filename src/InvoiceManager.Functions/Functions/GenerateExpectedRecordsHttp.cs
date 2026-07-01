using System.Net;
using InvoiceManager.Core;
using InvoiceManager.Core.Repositories;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace InvoiceManager.Functions.Functions;

public sealed class GenerateExpectedRecordsHttp(
    IInvoiceConfigurationRepository configurationRepository,
    ExpectedRecordGenerator generator,
    ILogger<GenerateExpectedRecordsHttp> logger)
{
    [Function("GenerateExpectedRecordsHttp")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Expected record generation triggered by HTTP request.");
        var configurations = await configurationRepository.ListActiveAsync(cancellationToken);
        foreach (var config in configurations)
            await generator.GenerateAsync(config, cancellationToken);
        logger.LogInformation("Expected record generation complete for {Count} configuration(s).", configurations.Count);
        return req.CreateResponse(HttpStatusCode.OK);
    }
}
