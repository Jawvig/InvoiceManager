using Azure.Core;
using Azure.Identity;
using InvoiceManager.AdminWeb.Services;
using InvoiceManager.Infrastructure.CosmosDb;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using Microsoft.Azure.Cosmos;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);

// Behind the Azure Container Apps ingress, TLS terminates at the proxy and the app receives
// plain HTTP with the original scheme in X-Forwarded-Proto. Honor it so HttpsRedirection and
// the OIDC callback URL are built as https:// (an http:// callback fails Entra validation).
// The ingress IP is dynamic and is the only route to the container, so the default
// known-proxy allowlist is cleared.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var configuredKeyVaultUri = builder.Configuration.GetValue<Uri?>("MicrosoftAuthorization:KeyVaultUri");
if (configuredKeyVaultUri is not null && !builder.Environment.IsEnvironment("Testing"))
{
    builder.Configuration.AddAzureKeyVault(configuredKeyVaultUri, new DefaultAzureCredential());
}

builder.Services
    .AddOptions<MicrosoftAuthorizationOptions>()
    .Bind(builder.Configuration.GetSection(MicrosoftAuthorizationOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<MicrosoftAuthorizationOptions>, MicrosoftAuthorizationOptionsValidator>();

builder.Services.AddSingleton<IMicrosoftAuthorizationStore>(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<MicrosoftAuthorizationOptions>>().Value;
    var secretStoreClient = new AzureKeyVaultSecretStoreClient(options.KeyVaultUri);
    return new KeyVaultMicrosoftAuthorizationStore(
        secretStoreClient,
        serviceProvider.GetRequiredService<IOptions<MicrosoftAuthorizationOptions>>());
});

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie()
    .AddOpenIdConnect();
builder.Services.AddSingleton<IConfigureOptions<OpenIdConnectOptions>, MicrosoftOpenIdConnectOptionsSetup>();

builder.Services.AddAuthorization();

// Shared credential for outbound service-to-service calls (the container binds it to the
// AdminWeb user-assigned managed identity via AZURE_CLIENT_ID). Used to mint a token for
// the Easy Auth-protected Functions app.
builder.Services.AddSingleton<TokenCredential>(new DefaultAzureCredential());

builder.Services.AddHttpClient<FunctionsHealthCheck>();
builder.Services.AddHttpClient<IExpectedRecordGenerationTrigger, FunctionsExpectedRecordGenerationTrigger>();
builder.Services.AddSingleton(_ => CosmosClientFactory.Create(builder.Configuration));
builder.Services
    .AddHealthChecks()
    .AddCheck<CosmosHealthCheck>("cosmos")
    .AddCheck<FunctionsHealthCheck>("functions");
builder.Services.AddRazorPages();

var app = builder.Build();

// Must run before anything that inspects the request scheme (HttpsRedirection, auth).
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapHealthChecks("/health");
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

public partial class Program
{
}

internal sealed class MicrosoftOpenIdConnectOptionsSetup
    : IConfigureNamedOptions<OpenIdConnectOptions>
{
    // The auth-code redemption may only request scopes for a SINGLE resource; the
    // Entra v2 token endpoint rejects a mixed-resource scope set with AADSTS28000.
    // We redeem for Microsoft Graph alone, whose only job here is to seed the MSAL
    // cache with a refresh token + account. Consent for the other resources (ARM
    // billing, Graph Files.ReadWrite.All) is gathered on the interactive authorize
    // leg below, so downstream AcquireTokenSilent calls can mint per-resource tokens
    // for either resource from that same refresh token. openid/profile/offline_access
    // are reserved scopes MSAL adds automatically.
    private static readonly string[] DownstreamScopes =
    [
        "User.Read"
    ];

    private readonly IOptions<MicrosoftAuthorizationOptions> microsoftAuthorizationOptions;

    public MicrosoftOpenIdConnectOptionsSetup(
        IOptions<MicrosoftAuthorizationOptions> microsoftAuthorizationOptions)
    {
        this.microsoftAuthorizationOptions = microsoftAuthorizationOptions;
    }

    public void Configure(string? name, OpenIdConnectOptions options)
    {
        if (name != OpenIdConnectDefaults.AuthenticationScheme)
        {
            return;
        }

        var authOptions = microsoftAuthorizationOptions.Value;
        options.Authority = authOptions.Authority;
        options.ClientId = authOptions.ClientId;
        options.ClientSecret = authOptions.ClientSecret;
        options.CallbackPath = "/signin-oidc";
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.SaveTokens = false;
        options.UsePkce = true;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("offline_access");
        options.Scope.Add("User.Read");
        options.Scope.Add("https://management.azure.com/user_impersonation");
        options.Scope.Add("https://graph.microsoft.com/Files.ReadWrite.All");
        options.Scope.Add("https://graph.microsoft.com/Mail.Read");
        options.TokenValidationParameters.NameClaimType = "name";

        options.Events.OnAuthorizationCodeReceived = async context =>
        {
            var currentAuthOptions = context.HttpContext.RequestServices
                .GetRequiredService<IOptions<MicrosoftAuthorizationOptions>>()
                .Value;

            var redirectUri = $"{context.Request.Scheme}://{context.Request.Host}{context.Options.CallbackPath}";
            var application = ConfidentialClientApplicationBuilder
                .Create(currentAuthOptions.ClientId)
                .WithClientSecret(currentAuthOptions.ClientSecret)
                .WithAuthority(currentAuthOptions.Authority)
                .WithRedirectUri(redirectUri)
                .Build();

            var authorizationStore = context.HttpContext.RequestServices
                .GetRequiredService<IMicrosoftAuthorizationStore>();
            MsalTokenCacheBinding.Bind(application.UserTokenCache, authorizationStore);

            var codeVerifier = context.TokenEndpointRequest?.GetParameter(OAuthConstants.CodeVerifierKey);
            if (string.IsNullOrWhiteSpace(codeVerifier) &&
                context.Properties is not null &&
                context.Properties.Items.TryGetValue(OAuthConstants.CodeVerifierKey, out var storedCodeVerifier))
            {
                codeVerifier = storedCodeVerifier;
            }

            if (string.IsNullOrWhiteSpace(codeVerifier))
            {
                throw new InvalidOperationException(
                    "The OpenID Connect authorization response did not include the PKCE code verifier.");
            }

            var result = await application
                .AcquireTokenByAuthorizationCode(DownstreamScopes, context.ProtocolMessage.Code)
                .WithPkceCodeVerifier(codeVerifier)
                .ExecuteAsync();

            context.HandleCodeRedemption(result.AccessToken, result.IdToken);
        };
    }

    public void Configure(OpenIdConnectOptions options)
    {
        Configure(Options.DefaultName, options);
    }
}

internal sealed class CosmosHealthCheck(CosmosClient cosmosClient, ILogger<CosmosHealthCheck> logger)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await cosmosClient.ReadAccountAsync().WaitAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Admin web Cosmos health check failed.");
            return HealthCheckResult.Unhealthy("Cosmos DB is not reachable.", ex);
        }
    }
}

internal sealed class FunctionsHealthCheck(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<FunctionsHealthCheck> logger)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var functionsBaseUrl = configuration.GetValue<Uri?>("Functions:BaseUrl");
        if (functionsBaseUrl is null)
        {
            return HealthCheckResult.Unhealthy("Functions:BaseUrl is not configured.");
        }

        try
        {
            var healthUri = new Uri(functionsBaseUrl, "/api/health");
            using var response = await httpClient.GetAsync(healthUri, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Healthy();
            }

            return HealthCheckResult.Unhealthy(
                $"Functions app health endpoint returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Admin web Functions health check failed.");
            return HealthCheckResult.Unhealthy("Functions app is not reachable.", ex);
        }
    }
}
