using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using InvoiceManager.AdminWeb.Pages;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.RazorPages;
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
        Assert.Contains("Sign in and authorize", body);
        Assert.DoesNotContain("Reset authorization", body);
        Assert.DoesNotContain("Set MicrosoftAuthorization", body);
    }

    [Fact]
    public async Task HomePage_RendersSignInAndResetActions_WhenAuthorizationIsCapturedAndUserIsNotSignedIn()
    {
        await using var factory = CreateConfiguredFactory(hasTokenCache: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains("Ready", body);
        Assert.Contains("Sign in", body);
        Assert.Contains("Reset authorization", body);
        Assert.DoesNotContain("Sign in and authorize", body);
    }

    [Fact]
    public async Task HomePageModel_ShowsAuthorizeAction_WhenUserIsSignedInAndAuthorizationIsNotCaptured()
    {
        var model = CreateIndexModel(hasTokenCache: false, isSignedIn: true);

        await model.OnGetAsync();

        Assert.True(model.ShowAuthorizeButton);
        Assert.Equal("Authorize", model.AuthorizeButtonCaption);
        Assert.True(model.IsSignedIn);
        Assert.False(model.IsAuthorizationCaptured);
    }

    [Fact]
    public async Task HomePageModel_HidesAuthorizeAction_WhenUserIsSignedInAndAuthorizationIsCaptured()
    {
        var model = CreateIndexModel(hasTokenCache: true, isSignedIn: true);

        await model.OnGetAsync();

        Assert.False(model.ShowAuthorizeButton);
        Assert.True(model.IsSignedIn);
        Assert.True(model.IsAuthorizationCaptured);
    }

    private static WebApplicationFactory<Program> CreateConfiguredFactory(bool hasTokenCache = false)
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
                    services.AddSingleton<IMicrosoftAuthorizationStore>(
                        new FakeMicrosoftAuthorizationStore(hasTokenCache));
                });
            });
    }

    private static IndexModel CreateIndexModel(bool hasTokenCache, bool isSignedIn)
    {
        var model = new IndexModel(
            new FakeMicrosoftAuthorizationStore(hasTokenCache),
            Options.Create(new MicrosoftAuthorizationOptions
            {
                TenantId = "11111111-1111-1111-1111-111111111111",
                ClientId = "22222222-2222-2222-2222-222222222222",
                ClientSecret = "client-secret",
                KeyVaultUri = new Uri("https://example.vault.azure.net/")
            }));

        var identity = isSignedIn
            ? new ClaimsIdentity([new Claim(ClaimTypes.Name, "Admin User")], "Test")
            : new ClaimsIdentity();
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };
        model.PageContext = new PageContext
        {
            HttpContext = httpContext
        };
        model.TempData = new TempDataDictionary(httpContext, new FakeTempDataProvider());

        return model;
    }

    private sealed class FakeTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context)
        {
            return new Dictionary<string, object>();
        }

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }

    private sealed class FakeMicrosoftAuthorizationStore : IMicrosoftAuthorizationStore
    {
        private readonly bool hasTokenCache;

        public FakeMicrosoftAuthorizationStore(bool hasTokenCache)
        {
            this.hasTokenCache = hasTokenCache;
        }

        public Task<bool> HasTokenCacheAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(hasTokenCache);
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
