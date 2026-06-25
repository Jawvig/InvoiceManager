using Azure.Identity;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddRazorPages();

var app = builder.Build();

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
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

public partial class Program
{
}

internal sealed class MicrosoftOpenIdConnectOptionsSetup
    : IConfigureNamedOptions<OpenIdConnectOptions>
{
    private static readonly string[] DownstreamScopes =
    [
        "User.Read",
        "https://management.azure.com/user_impersonation",
        "offline_access"
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
