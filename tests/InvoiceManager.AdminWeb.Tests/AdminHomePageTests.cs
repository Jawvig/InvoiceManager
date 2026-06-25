using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace InvoiceManager.AdminWeb.Tests;

public sealed class AdminHomePageTests
{
    [Fact]
    public async Task HomePage_FailsFast_WhenAuthorizationConfigurationIsMissing()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.Sources.Clear();
                });
            });

        var exception = Assert.Throws<OptionsValidationException>(
            () => factory.CreateClient());

        Assert.Contains("MicrosoftAuthorization:TenantId is required.", exception.Failures);
    }

    [Fact]
    public async Task HomePage_RendersStatus_WhenAuthorizationConfigurationIsPresent()
    {
        await using var factory = CreateConfiguredFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains("Microsoft authorization", body);
        Assert.Contains("Not captured", body);
        Assert.DoesNotContain("Set MicrosoftAuthorization", body);
    }

    private static WebApplicationFactory<Program> CreateConfiguredFactory()
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.Sources.Clear();
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["MicrosoftAuthorization:TenantId"] = "11111111-1111-1111-1111-111111111111",
                        ["MicrosoftAuthorization:ClientId"] = "22222222-2222-2222-2222-222222222222",
                        ["MicrosoftAuthorization:ClientSecret"] = "client-secret",
                        ["MicrosoftAuthorization:KeyVaultUri"] = "https://example.vault.azure.net/",
                        ["MicrosoftAuthorization:TokenCacheSecretName"] = "MicrosoftAuthorization--MsalTokenCache"
                    });
                });
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<IMicrosoftAuthorizationStore>(new FakeMicrosoftAuthorizationStore());
                });
            });
    }

    private sealed class FakeMicrosoftAuthorizationStore : IMicrosoftAuthorizationStore
    {
        public Task<bool> HasTokenCacheAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<byte[]?> ReadTokenCacheAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<byte[]?>(null);
        }

        public Task SaveTokenCacheAsync(byte[] tokenCache, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ClearTokenCacheAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
