using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using Microsoft.Extensions.Options;

namespace InvoiceManager.Infrastructure.Tests;

public sealed class MicrosoftAuthorizationOptionsValidatorTests
{
    [Fact]
    public void Validate_Fails_WhenTenantIdIsMissing()
    {
        var validator = new MicrosoftAuthorizationOptionsValidator();
        var result = validator.Validate(null, new MicrosoftAuthorizationOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            KeyVaultUri = new Uri("https://example.vault.azure.net/")
        });

        Assert.True(result.Failed);
        Assert.Contains(
            "MicrosoftAuthorization:TenantId is required.",
            result.Failures);
    }

    [Fact]
    public void Validate_Fails_WhenClientIdIsMissing()
    {
        var validator = new MicrosoftAuthorizationOptionsValidator();
        var result = validator.Validate(null, new MicrosoftAuthorizationOptions
        {
            TenantId = "tenant-id",
            ClientSecret = "client-secret",
            KeyVaultUri = new Uri("https://example.vault.azure.net/")
        });

        Assert.True(result.Failed);
        Assert.Contains(
            "MicrosoftAuthorization:ClientId is required.",
            result.Failures);
    }

    [Fact]
    public void Validate_Fails_WhenClientSecretIsMissing()
    {
        var validator = new MicrosoftAuthorizationOptionsValidator();
        var result = validator.Validate(null, new MicrosoftAuthorizationOptions
        {
            TenantId = "tenant-id",
            ClientId = "client-id",
            KeyVaultUri = new Uri("https://example.vault.azure.net/")
        });

        Assert.True(result.Failed);
        Assert.Contains(
            "MicrosoftAuthorization:ClientSecret is required.",
            result.Failures);
    }

    [Fact]
    public void Validate_Fails_WhenKeyVaultUriIsMissing()
    {
        var validator = new MicrosoftAuthorizationOptionsValidator();
        var result = validator.Validate(null, new MicrosoftAuthorizationOptions
        {
            TenantId = "tenant-id",
            ClientId = "client-id",
            ClientSecret = "client-secret"
        });

        Assert.True(result.Failed);
        Assert.Contains(
            "MicrosoftAuthorization:KeyVaultUri is required.",
            result.Failures);
    }

    [Fact]
    public void Validate_Succeeds_WhenRequiredOptionsArePresent()
    {
        var validator = new MicrosoftAuthorizationOptionsValidator();
        var result = validator.Validate(null, new MicrosoftAuthorizationOptions
        {
            TenantId = "tenant-id",
            ClientId = "client-id",
            ClientSecret = "client-secret",
            KeyVaultUri = new Uri("https://example.vault.azure.net/")
        });

        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public async Task UnavailableStore_ReportsMissingPersistenceWithClearError()
    {
        var store = new UnavailableMicrosoftAuthorizationStore();

        Assert.False(await store.HasTokenCacheAsync());
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SaveTokenCacheAsync([1, 2, 3]));
        Assert.Contains("MicrosoftAuthorization:KeyVaultUri is required", exception.Message);
    }
}
