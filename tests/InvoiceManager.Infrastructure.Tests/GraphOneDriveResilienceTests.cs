using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using InvoiceManager.Core;
using InvoiceManager.Core.Integrations;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using InvoiceManager.Infrastructure.OneDrive;
using InvoiceManager.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using NodaMoney;

namespace InvoiceManager.Infrastructure.Tests;

/// <summary>
/// Verifies that the Graph OneDrive client, as registered by
/// <see cref="GraphOneDriveRegistration.AddGraphOneDriveIntegration"/>, retries
/// throttling responses (429/503) via the standard resilience handler rather than
/// any bespoke retry loop in the integration. This exercises the wiring, not the
/// library internals — the primary handler scripts a throttle then a success.
/// </summary>
public sealed class GraphOneDriveResilienceTests
{
    private static readonly OneDriveFolder Folder =
        new("drive-1", "Drive One", "folder-1", "/Bills/Microsoft 365");

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Search_RetriesThrottling_ThroughTheStandardResilienceHandler(HttpStatusCode throttleStatus)
    {
        var handler = new StubHttpMessageHandler((_, index) => index == 0
            ? Throttled(throttleStatus)
            : Json(HttpStatusCode.OK, Children(
                "2026-07-10 Microsoft 365 Business Basic G152207778 £11.59 exc.pdf")));

        await using var provider = BuildProvider(handler);
        var integration = provider.GetRequiredService<IOneDriveIntegration>();

        var result = await integration.SearchAsync(new OneDriveSearchRequest(Folder, Criteria()));

        Assert.True(result is OneDriveMatch, $"Expected OneDriveMatch but got {result}.");
        // One throttled attempt, then a successful retry: proves the pipeline retried.
        Assert.Equal(2, handler.Requests.Count);
    }

    private static ServiceProvider BuildProvider(StubHttpMessageHandler primaryHandler)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMicrosoftTokenProvider>(new FakeMicrosoftTokenProvider());
        services.AddSingleton(new InvoiceFilename(
            new InvoiceFilenameSettings { Culture = CultureInfo.GetCultureInfo("en-GB") }));
        services.AddGraphOneDriveIntegration()
            .ConfigurePrimaryHttpMessageHandler(() => primaryHandler);
        return services.BuildServiceProvider();
    }

    private static OneDriveSearchCriteria Criteria() => new(
        ExpectedDate: new DateOnly(2026, 7, 10),
        DateToleranceDays: 3,
        AmountMatchingCriteria: new AmountMatchingCriteria(new Money(11.59m, "GBP"), 0m),
        InvoiceDescription: "Microsoft 365 Business Basic");

    private static HttpResponseMessage Throttled(HttpStatusCode status)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent("throttled") };
        // Retry immediately: honoured by the resilience handler, and keeps the test fast.
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
        return response;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string Children(string name) =>
        $$"""{ "value": [{ "id": "id-1", "name": {{System.Text.Json.JsonSerializer.Serialize(name)}}, "webUrl": "https://example/id-1" }] }""";
}
