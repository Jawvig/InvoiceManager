using System.Net.Http.Headers;
using Azure.Core;

namespace InvoiceManager.AdminWeb.Services;

/// <summary>
/// Triggers the Functions app's <c>GenerateExpectedRecordsHttp</c> endpoint on
/// behalf of an operator. The Functions base address is supplied by Aspire via
/// the <c>Functions:BaseUrl</c> configuration value (see the AppHost).
/// </summary>
public interface IExpectedRecordGenerationTrigger
{
    Task<ExpectedRecordGenerationTriggerResult> TriggerAsync(CancellationToken cancellationToken);
}

/// <summary>The Functions endpoint accepted the request and returned a success status.</summary>
public sealed record ExpectedRecordGenerationTriggered(int StatusCode);

/// <summary>No Functions base URL was configured, so no request could be made.</summary>
public sealed record ExpectedRecordGenerationNotConfigured;

/// <summary>A request was made but the Functions app was unreachable or returned a non-success status.</summary>
public sealed record ExpectedRecordGenerationFailed(string Message);

/// <summary>
/// Outcome of asking the Functions app to generate expected records. Modelled as a
/// union so callers cannot forget to handle a failure mode.
/// </summary>
public union ExpectedRecordGenerationTriggerResult(
    ExpectedRecordGenerationTriggered,
    ExpectedRecordGenerationNotConfigured,
    ExpectedRecordGenerationFailed);

public sealed class FunctionsExpectedRecordGenerationTrigger(
    HttpClient httpClient,
    IConfiguration configuration,
    TokenCredential credential,
    ILogger<FunctionsExpectedRecordGenerationTrigger> logger)
    : IExpectedRecordGenerationTrigger
{
    // Isolated-worker HTTP functions are exposed under /api/{FunctionName}.
    private const string TriggerPath = "/api/GenerateExpectedRecordsHttp";

    public async Task<ExpectedRecordGenerationTriggerResult> TriggerAsync(CancellationToken cancellationToken)
    {
        var functionsBaseUrl = configuration.GetValue<Uri?>("Functions:BaseUrl");
        if (functionsBaseUrl is null)
        {
            return new ExpectedRecordGenerationNotConfigured();
        }

        var triggerUri = new Uri(functionsBaseUrl, TriggerPath);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, triggerUri);
            await AuthorizeAsync(request, cancellationToken);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new ExpectedRecordGenerationTriggered((int)response.StatusCode);
            }

            return new ExpectedRecordGenerationFailed(
                $"The Functions app returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Triggering expected record generation failed.");
            return new ExpectedRecordGenerationFailed("The Functions app is not reachable.");
        }
    }

    // Deployed environments protect the Functions app with Easy Auth (Entra ID) and set
    // Functions:Scope to its audience; acquire an app-only token via the AdminWeb managed
    // identity and send it as a bearer. Locally (Aspire) the scope is unset and the
    // request is sent unauthenticated against the anonymous local host.
    private async Task AuthorizeAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var scope = configuration.GetValue<string?>("Functions:Scope");
        if (string.IsNullOrWhiteSpace(scope))
        {
            return;
        }

        var token = await credential.GetTokenAsync(new TokenRequestContext([scope]), cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
    }
}
