using InvoiceManager.Infrastructure.FreeAgentAuthorization;
using Microsoft.Extensions.Options;

namespace InvoiceManager.Infrastructure.Tests;

public sealed class FreeAgentAuthorizationOptionsValidatorTests
{
    [Fact]
    public void Validate_Fails_WhenClientIdIsMissing()
    {
        var validator = new FreeAgentAuthorizationOptionsValidator();
        var result = validator.Validate(null, new FreeAgentAuthorizationOptions
        {
            ClientSecret = "client-secret"
        });

        Assert.True(result.Failed);
        Assert.Contains("FreeAgentAuthorization:ClientId is required.", result.Failures);
    }

    [Fact]
    public void Validate_Fails_WhenClientSecretIsMissing()
    {
        var validator = new FreeAgentAuthorizationOptionsValidator();
        var result = validator.Validate(null, new FreeAgentAuthorizationOptions
        {
            ClientId = "client-id"
        });

        Assert.True(result.Failed);
        Assert.Contains("FreeAgentAuthorization:ClientSecret is required.", result.Failures);
    }

    [Fact]
    public void Validate_Fails_WhenRefreshTokenSecretNameIsBlank()
    {
        var validator = new FreeAgentAuthorizationOptionsValidator();
        var result = validator.Validate(null, new FreeAgentAuthorizationOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RefreshTokenSecretName = "   "
        });

        Assert.True(result.Failed);
        Assert.Contains("FreeAgentAuthorization:RefreshTokenSecretName is required.", result.Failures);
    }

    [Fact]
    public void Validate_Succeeds_WhenRequiredOptionsArePresent()
    {
        var validator = new FreeAgentAuthorizationOptionsValidator();
        var result = validator.Validate(null, new FreeAgentAuthorizationOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret"
        });

        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void HasClientConfiguration_IsFalse_WhenEitherValueIsMissing()
    {
        Assert.False(new FreeAgentAuthorizationOptions { ClientSecret = "s" }.HasClientConfiguration);
        Assert.False(new FreeAgentAuthorizationOptions { ClientId = "c" }.HasClientConfiguration);
        Assert.True(new FreeAgentAuthorizationOptions { ClientId = "c", ClientSecret = "s" }.HasClientConfiguration);
    }
}
