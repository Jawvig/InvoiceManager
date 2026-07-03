using System.Net;
using InvoiceManager.Core;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace InvoiceManager.Functions.Functions;

public sealed class GenerateExpectedRecordsHttp(
    ExpectedRecordGenerator generator,
    ILogger<GenerateExpectedRecordsHttp> logger)
{
    private const HttpStatusCode MultiStatus = (HttpStatusCode)207;

    [Function("GenerateExpectedRecordsHttp")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Expected record generation triggered by HTTP request.");
        var results = await generator.GenerateForAllActiveAsync(cancellationToken);
        logger.LogInformation(
            "Expected record generation complete: {SucceededCount} succeeded, {FailedCount} failed.",
            results.Count(r => r is GenerationSucceeded),
            results.Count(r => r is GenerationFailed));

        var body = results
            .Select(result => result switch
            {
                GenerationSucceeded succeeded =>
                    new ConfigurationResultDto(succeeded.ConfigurationId.Value, "Succeeded", null),
                GenerationFailed failed =>
                    new ConfigurationResultDto(failed.ConfigurationId.Value, "Failed", failed.Exception.Message),
            })
            .ToList();

        var response = req.CreateResponse();
        await response.WriteAsJsonAsync(body, cancellationToken);
        // WriteAsJsonAsync sets 200; override after writing.
        response.StatusCode = MultiStatus;
        return response;
    }

    // Serialization shape for the 207 Multi-Status body; Error is null for successes.
    private sealed record ConfigurationResultDto(string ConfigurationId, string Status, string? Error);
}
