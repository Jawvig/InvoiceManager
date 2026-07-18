using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using InvoiceManager.AdminWeb.Pages;
using InvoiceManager.AdminWeb.Services;
using InvoiceManager.Core.Repositories;
using InvoiceManager.TestSupport;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Text.Encodings.Web;

namespace InvoiceManager.AdminWeb.Tests;

public sealed class AdminHomePageTests
{
    [Fact]
    public async Task SignIn_RequestsMailReadScope_SoConsentCoversTheEmailInvoiceSource()
    {
        await using var factory = CreateConfiguredFactory();
        using var scope = factory.Services.CreateScope();

        var options = scope.ServiceProvider
            .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get("WorkflowAuthorization");

        Assert.Contains("https://graph.microsoft.com/Mail.Read", options.Scope);
    }

    [Fact]
    public async Task OrdinarySignIn_DoesNotWriteTheSharedWorkflowTokenCache()
    {
        await using var factory = CreateConfiguredFactory();
        using var scope = factory.Services.CreateScope();
        var monitor = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>();

        var ordinary = monitor.Get(OpenIdConnectDefaults.AuthenticationScheme).Events.OnAuthorizationCodeReceived;
        var workflow = monitor.Get("WorkflowAuthorization").Events.OnAuthorizationCodeReceived;
        Assert.NotNull(ordinary);
        Assert.NotNull(workflow);
        Assert.NotEqual(ordinary.Method, workflow.Method);
    }

    [Fact]
    public async Task SignedInUserOutsideAdminGroup_IsForbidden()
    {
        await using var factory = CreateConfiguredFactory(isGroupMember: false);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/");

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedUser_IsChallengedForTheWholeSite()
    {
        await using var factory = CreateConfiguredFactory(isAuthenticated: false);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/Configurations");

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

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

        var exception = Assert.ThrowsAny<Exception>(
            () => factory.CreateClient());

        Assert.Contains("MicrosoftAuthorization:TenantId is required.", exception.ToString());
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
        Assert.Contains("Capture workflow authorization", body);
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
        Assert.Contains("Replace workflow authorization", body);
        Assert.Contains("Reset authorization", body);
        Assert.DoesNotContain("Capture workflow authorization", body);
    }

    [Fact]
    public async Task HomePageModel_ShowsAuthorizeAction_WhenUserIsSignedInAndAuthorizationIsNotCaptured()
    {
        var model = CreateIndexModel(hasTokenCache: false, isSignedIn: true);

        await model.OnGetAsync();

        Assert.True(model.ShowAuthorizeButton);
        Assert.Equal("Capture workflow authorization", model.AuthorizeButtonCaption);
        Assert.True(model.IsSignedIn);
        Assert.False(model.IsAuthorizationCaptured);
    }

    [Fact]
    public async Task HomePageModel_OffersExplicitReplacement_WhenAuthorizationIsCaptured()
    {
        var model = CreateIndexModel(hasTokenCache: true, isSignedIn: true);

        await model.OnGetAsync();

        Assert.True(model.ShowAuthorizeButton);
        Assert.Equal("Replace workflow authorization", model.AuthorizeButtonCaption);
        Assert.True(model.IsSignedIn);
        Assert.True(model.IsAuthorizationCaptured);
    }

    private static WebApplicationFactory<Program> CreateConfiguredFactory(
        bool hasTokenCache = false,
        bool isGroupMember = true,
        bool isAuthenticated = true,
        bool useTestAuthentication = true)
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
                        ["MicrosoftAuthorization:TokenCacheSecretName"] = "MicrosoftAuthorization--MsalTokenCache",
                        ["AdminAuthorization:GroupObjectId"] = "33333333-3333-3333-3333-333333333333"
                    });
                });
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<IMicrosoftAuthorizationStore>(
                        new FakeMicrosoftAuthorizationStore(hasTokenCache));
                    if (useTestAuthentication)
                    {
                        services.AddSingleton(new TestIdentity(isAuthenticated, isGroupMember));
                        services.AddAuthentication(options =>
                        {
                            options.DefaultAuthenticateScheme = "Test";
                            options.DefaultChallengeScheme = "Test";
                            options.DefaultForbidScheme = "Test";
                        }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
                    }
                });
            });
    }

    private static IndexModel CreateIndexModel(
        bool hasTokenCache,
        bool isSignedIn,
        IExpectedRecordGenerationTrigger? expectedRecordGenerationTrigger = null)
    {
        var model = new IndexModel(
            new FakeMicrosoftAuthorizationStore(hasTokenCache),
            Options.Create(new MicrosoftAuthorizationOptions
            {
                TenantId = "11111111-1111-1111-1111-111111111111",
                ClientId = "22222222-2222-2222-2222-222222222222",
                ClientSecret = "client-secret",
                KeyVaultUri = new Uri("https://example.vault.azure.net/")
            }),
            expectedRecordGenerationTrigger ?? new FakeExpectedRecordGenerationTrigger());

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

    [Fact]
    public async Task GenerateExpectedRecords_TriggersFunction_AndSurfacesResultAsStatusMessage()
    {
        var trigger = new FakeExpectedRecordGenerationTrigger(
            new ExpectedRecordGenerationTriggered(207));
        var model = CreateIndexModel(hasTokenCache: true, isSignedIn: true, trigger);

        var result = await model.OnPostGenerateExpectedRecordsAsync();

        Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToPageResult>(result);
        Assert.True(trigger.WasTriggered);
        Assert.Equal(
            "Expected record generation was triggered (HTTP 207).",
            model.TempData["StatusMessage"]);
    }

    [Fact]
    public async Task GenerateExpectedRecords_ReportsMissingConfiguration_WhenFunctionsUrlIsNotConfigured()
    {
        var trigger = new FakeExpectedRecordGenerationTrigger(
            new ExpectedRecordGenerationNotConfigured());
        var model = CreateIndexModel(hasTokenCache: true, isSignedIn: true, trigger);

        await model.OnPostGenerateExpectedRecordsAsync();

        Assert.Equal(
            "The Functions app URL is not configured, so expected record generation could not be triggered.",
            model.TempData["StatusMessage"]);
    }

    private sealed class FakeExpectedRecordGenerationTrigger : IExpectedRecordGenerationTrigger
    {
        private readonly ExpectedRecordGenerationTriggerResult result;

        public FakeExpectedRecordGenerationTrigger(
            ExpectedRecordGenerationTriggerResult? result = null)
        {
            this.result = result ?? new ExpectedRecordGenerationTriggered(207);
        }

        public bool WasTriggered { get; private set; }

        public Task<ExpectedRecordGenerationTriggerResult> TriggerAsync(CancellationToken cancellationToken)
        {
            WasTriggered = true;
            return Task.FromResult(result);
        }
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

    [Fact]
    public async Task ConfigurationList_RemainsAvailableWithoutWorkflowAuthorization_WhileMutationsAreDisabled()
    {
        await using var factory = CreateConfiguredFactory(hasTokenCache: false)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IInvoiceConfigurationRepository>(
                    new FakeConfigurationRepository(Configurations.Build(isActive: false)));
                services.AddSingleton<IInvoiceRecordRepository>(new InMemoryInvoiceRecordRepository());
            }));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/Configurations");
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains("Test Invoice", body);
        Assert.Contains("Workflow authorization is not captured", body);
        Assert.Contains("primary-action disabled", body);
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        TestIdentity testIdentity)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!testIdentity.IsAuthenticated)
                return Task.FromResult(AuthenticateResult.NoResult());

            const string groupId = "33333333-3333-3333-3333-333333333333";
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "Admin User"),
                new Claim(ClaimTypes.NameIdentifier, "44444444-4444-4444-4444-444444444444"),
                new Claim("oid", "44444444-4444-4444-4444-444444444444"),
                new Claim("admin_group", groupId),
                new Claim("groups", testIdentity.IsGroupMember ? groupId : "55555555-5555-5555-5555-555555555555"),
            ], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed record TestIdentity(bool IsAuthenticated, bool IsGroupMember);
}
