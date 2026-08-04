using Microsoft.Extensions.Options;

namespace InvoiceManager.Infrastructure;

/// <summary>
/// The single Azure Key Vault this application uses for every secret-backed store
/// (Microsoft authorization, FreeAgent authorization, and any future one). Shared
/// rather than owned by any one authorization options class, since it is not
/// specific to any of them.
/// </summary>
public sealed class KeyVaultOptions
{
    public const string SectionName = "KeyVault";

    public Uri Uri { get; set; } = null!;

    public bool HasPersistentStore => Uri is not null;
}

public sealed class KeyVaultOptionsValidator : IValidateOptions<KeyVaultOptions>
{
    public ValidateOptionsResult Validate(string? name, KeyVaultOptions options)
    {
        return options.Uri is null
            ? ValidateOptionsResult.Fail("KeyVault:Uri is required.")
            : ValidateOptionsResult.Success;
    }
}
