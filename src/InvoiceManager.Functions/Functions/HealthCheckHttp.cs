using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace InvoiceManager.Functions.Functions;

public sealed class HealthCheckHttp(
    CosmosClient cosmosClient,
    ILogger<HealthCheckHttp> logger)
{
    [Function("HealthCheckHttp")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var response = req.CreateResponse();

        try
        {
            await cosmosClient.ReadAccountAsync().WaitAsync(cancellationToken);
            await response.WriteAsJsonAsync(new HealthCheckDto("Healthy"), cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Functions health check failed.");
            response.StatusCode = HttpStatusCode.ServiceUnavailable;
            await response.WriteAsJsonAsync(new HealthCheckDto("Unhealthy"), cancellationToken);
            return response;
        }
    }

    private sealed record HealthCheckDto(string Status);
}
