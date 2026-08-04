namespace InvoiceManager.Infrastructure.FreeAgentAuthorization;

/// <summary>
/// The fixed FreeAgent hosts and derived endpoints for each <see cref="FreeAgentEnvironment"/>.
/// Every URL FreeAgent code needs is computed from these, rather than read from separate
/// configuration - production code can never be pointed at an unrecognised host.
/// </summary>
public static class FreeAgentHosts
{
    public const string SandboxHost = "api.sandbox.freeagent.com";
    public const string ProductionHost = "api.freeagent.com";

    public static string Host(FreeAgentEnvironment environment) => environment switch
    {
        FreeAgentEnvironment.Sandbox => SandboxHost,
        FreeAgentEnvironment.Production => ProductionHost,
        _ => throw new ArgumentOutOfRangeException(
            nameof(environment), environment, "Unrecognised FreeAgent environment.")
    };

    public static Uri ApiBaseUri(FreeAgentEnvironment environment) =>
        new($"https://{Host(environment)}/v2/");

    public static Uri AuthorizationEndpoint(FreeAgentEnvironment environment) =>
        new($"https://{Host(environment)}/v2/approve_app");

    public static Uri TokenEndpoint(FreeAgentEnvironment environment) =>
        new($"https://{Host(environment)}/v2/token_endpoint");
}
