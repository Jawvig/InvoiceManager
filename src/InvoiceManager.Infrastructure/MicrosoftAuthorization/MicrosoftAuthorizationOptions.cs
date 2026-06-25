using Microsoft.Extensions.Options;

namespace InvoiceManager.Infrastructure.MicrosoftAuthorization;

public sealed class MicrosoftAuthorizationOptions
{
    public const string SectionName = "MicrosoftAuthorization";

    public const string DefaultTokenCacheSecretName = "MicrosoftAuthorization--MsalTokenCache";

    public string? TenantId { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public Uri KeyVaultUri { get; set; } = null!;

    public string TokenCacheSecretName { get; set; } = DefaultTokenCacheSecretName;

    public string Authority => string.IsNullOrWhiteSpace(TenantId)
        ? string.Empty
        : $"https://login.microsoftonline.com/{TenantId}/v2.0";

    public bool HasEntraConfiguration =>
        !string.IsNullOrWhiteSpace(TenantId) &&
        !string.IsNullOrWhiteSpace(ClientId);

    public bool HasClientSecret => !string.IsNullOrWhiteSpace(ClientSecret);

    public bool HasPersistentStore => KeyVaultUri is not null;
}

public sealed class MicrosoftAuthorizationOptionsValidator
    : IValidateOptions<MicrosoftAuthorizationOptions>
{
    public ValidateOptionsResult Validate(string? name, MicrosoftAuthorizationOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.TenantId))
        {
            failures.Add("MicrosoftAuthorization:TenantId is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add("MicrosoftAuthorization:ClientId is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            failures.Add("MicrosoftAuthorization:ClientSecret is required.");
        }

        if (options.KeyVaultUri is null)
        {
            failures.Add("MicrosoftAuthorization:KeyVaultUri is required.");
        }

        if (string.IsNullOrWhiteSpace(options.TokenCacheSecretName))
        {
            failures.Add("MicrosoftAuthorization:TokenCacheSecretName is required.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
