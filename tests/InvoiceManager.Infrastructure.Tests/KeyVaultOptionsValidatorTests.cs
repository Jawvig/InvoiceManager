using Microsoft.Extensions.Options;

namespace InvoiceManager.Infrastructure.Tests;

public sealed class KeyVaultOptionsValidatorTests
{
    [Fact]
    public void Validate_Fails_WhenUriIsMissing()
    {
        var validator = new KeyVaultOptionsValidator();
        var result = validator.Validate(null, new KeyVaultOptions());

        Assert.True(result.Failed);
        Assert.Contains("KeyVault:Uri is required.", result.Failures);
    }

    [Fact]
    public void Validate_Succeeds_WhenUriIsPresent()
    {
        var validator = new KeyVaultOptionsValidator();
        var result = validator.Validate(null, new KeyVaultOptions
        {
            Uri = new Uri("https://example.vault.azure.net/")
        });

        Assert.Equal(ValidateOptionsResult.Success, result);
    }
}
