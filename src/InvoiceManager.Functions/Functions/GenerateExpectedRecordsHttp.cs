using System.Net;
using InvoiceManager.Core;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace InvoiceManager.Functions.Functions;

public sealed class GenerateExpectedRecordsHttp(
    ExpectedRecordGenerator generator,
    DueInvoiceProcessor processor,
    ILogger<GenerateExpectedRecordsHttp> logger)
{
    private const HttpStatusCode MultiStatus = (HttpStatusCode)207;

    [Function("GenerateExpectedRecordsHttp")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Expected record generation triggered by HTTP request.");
        var generationResults = await generator.GenerateForAllActiveAsync(cancellationToken);
        logger.LogInformation(
            "Expected record generation complete: {SucceededCount} succeeded, {FailedCount} failed.",
            generationResults.Count(r => r is GenerationSucceeded),
            generationResults.Count(r => r is GenerationFailed));

        var processingResults = await processor.ProcessDueAsync(cancellationToken);
        logger.LogInformation(
            "Due invoice processing complete: {SavedCount} saved, {NotYetFoundCount} not yet found, " +
            "{NotFoundCount} not found, {FailedCount} failed.",
            processingResults.Count(r => r is ProcessingSucceeded),
            processingResults.Count(r => r is ProcessingNotYetFound),
            processingResults.Count(r => r is ProcessingNotFound),
            processingResults.Count(r => r is ProcessingFailed));

        var body = new RunResultDto(
            generationResults.Select(result => result switch
            {
                GenerationSucceeded succeeded => new ConfigurationResultDto(succeeded.ConfigurationId.Value, "Succeeded", null),
                GenerationFailed failed => new ConfigurationResultDto(failed.ConfigurationId.Value, "Failed", failed.Exception.Message),
            }).ToList(),
            processingResults.Select(result => result switch
            {
                ProcessingSucceeded saved => new RecordResultDto(saved.RecordId.Value, "SavedToOneDrive", null),
                ProcessingNotYetFound notYetFound => new RecordResultDto(notYetFound.RecordId.Value, "NotYetFound", null),
                ProcessingNotFound notFound => new RecordResultDto(notFound.RecordId.Value, "NotFound", null),
                ProcessingFailed failed => new RecordResultDto(failed.RecordId.Value, "Failed", failed.Exception.Message),
            }).ToList());

        var response = req.CreateResponse();
        await response.WriteAsJsonAsync(body, cancellationToken);
        // WriteAsJsonAsync sets 200; override after writing.
        response.StatusCode = MultiStatus;
        return response;
    }

    // Serialization shapes for the 207 Multi-Status body; Error is null for successes.
    private sealed record RunResultDto(
        IReadOnlyList<ConfigurationResultDto> Generation,
        IReadOnlyList<RecordResultDto> Processing);

    private sealed record ConfigurationResultDto(string ConfigurationId, string Status, string? Error);

    private sealed record RecordResultDto(string RecordId, string Status, string? Error);
}
