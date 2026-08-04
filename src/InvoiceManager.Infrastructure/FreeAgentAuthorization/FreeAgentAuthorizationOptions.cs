using Microsoft.Extensions.Options;

namespace InvoiceManager.Infrastructure.FreeAgentAuthorization;

public sealed class FreeAgentAuthorizationOptions
{
    public const string SectionName = "FreeAgentAuthorization";

    public const string DefaultRefreshTokenSecretName = "FreeAgentAuthorization--RefreshToken";

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public string RefreshTokenSecretName { get; set; } = DefaultRefreshTokenSecretName;

    public bool HasClientConfiguration =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

public sealed class FreeAgentAuthorizationOptionsValidator : IValidateOptions<FreeAgentAuthorizationOptions>
{
    public ValidateOptionsResult Validate(string? name, FreeAgentAuthorizationOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add("FreeAgentAuthorization:ClientId is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            failures.Add("FreeAgentAuthorization:ClientSecret is required.");
        }

        if (string.IsNullOrWhiteSpace(options.RefreshTokenSecretName))
        {
            failures.Add("FreeAgentAuthorization:RefreshTokenSecretName is required.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
