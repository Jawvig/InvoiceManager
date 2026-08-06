using Azure.Identity;
using InvoiceManager.Infrastructure.FreeAgentAuthorization;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InvoiceManager.Integrations.FreeAgent.IntegrationTests;

/// <summary>
/// Opt-in: exercises the real FreeAgent sandbox API end to end (Key Vault refresh token -&gt;
/// FreeAgentTokenProvider -&gt; FreeAgentApiClient -&gt; FreeAgentContactDirectory), the way a
/// StubHttpMessageHandler-based unit test never can. This is deliberately the lightest-weight
/// integration test for FreeAgent in this repo: unlike the Microsoft/Graph equivalent
/// (ConfigurationEditLazyBillingTests.cs et al. in InvoiceManager.AdminWeb.PlaywrightTests),
/// FreeAgent's OAuth is a plain rotating refresh token already sitting in Key Vault once an
/// administrator has completed authorization once via AdminWeb - no interactive browser/OIDC
/// flow is needed to *use* it, only to originally capture it - so this test talks to
/// FreeAgentApiClient directly rather than booting the whole AppHost through a browser.
///
/// Requires:
/// - The developer signed in to Azure with access to the test environment's Key Vault
///   (`az login`), the same prerequisite as the Cosmos/Microsoft integration tests.
/// - `KeyVault:Uri` configured - reuses whatever InvoiceManager.AdminWeb's local user secrets
///   already have set for Aspire, so no separate setup is needed if Aspire already runs locally.
/// - A FreeAgent refresh token already captured against the sandbox company via AdminWeb's
///   /Authorization page (FreeAgent section) - see docs/deployment.md.
///
/// Excluded from CI by the same `Category=Integration` filter as every other opt-in sandbox test
/// in this repo (see AGENTS.md) - treat a green run as informative, not authoritative.
/// </summary>
[Trait("Category", "Integration")]
public sealed class FreeAgentContactDirectorySandboxTests
{
    [Fact]
    public async Task SearchAsync_SucceedsAgainstTheRealSandboxApi()
    {
        var localConfiguration = new ConfigurationBuilder()
            .AddUserSecrets("InvoiceManager.AdminWeb")
            .AddEnvironmentVariables()
            .Build();

        var keyVaultUri = localConfiguration.GetValue<Uri?>("KeyVault:Uri")
            ?? throw new InvalidOperationException(
                "KeyVault:Uri is not configured. Set it via `dotnet user-secrets set KeyVault:Uri " +
                "<test-environment-key-vault-uri> --project src/InvoiceManager.AdminWeb` (the same " +
                "value Aspire already uses locally), and be signed in to Azure with access to it " +
                "(`az login`).");

        // FreeAgentAuthorization:ClientId/ClientSecret are not local secrets - AdminWeb's own
        // Program.cs loads them straight from Key Vault at startup (FreeAgentAuthorization--ClientId
        // / FreeAgentAuthorization--ClientSecret) via this same provider, so this test does the same
        // rather than requiring them to be duplicated into local user secrets.
        var configuration = new ConfigurationBuilder()
            .AddConfiguration(localConfiguration)
            .AddAzureKeyVault(keyVaultUri, new DefaultAzureCredential())
            .Build();

        var authorizationOptions = configuration.GetSection(FreeAgentAuthorizationOptions.SectionName)
            .Get<FreeAgentAuthorizationOptions>() ?? new FreeAgentAuthorizationOptions();
        var freeAgentOptions = configuration.GetSection(FreeAgentOptions.SectionName)
            .Get<FreeAgentOptions>() ?? new FreeAgentOptions { Environment = FreeAgentEnvironment.Sandbox };

        var secretStoreClient = new AzureKeyVaultSecretStoreClient(keyVaultUri);
        var authorizationStore = new KeyVaultFreeAgentAuthorizationStore(
            secretStoreClient, Options.Create(authorizationOptions));

        if (!await authorizationStore.HasRefreshTokenAsync())
        {
            throw new InvalidOperationException(
                "No FreeAgent refresh token is stored in Key Vault. Complete FreeAgent authorization " +
                "against the sandbox company via AdminWeb's /Authorization page (FreeAgent section) first.");
        }

        using var tokenHttpClient = new HttpClient();
        tokenHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("InvoiceManager-IntegrationTests/1.0");
        var tokenProvider = new FreeAgentTokenProvider(
            tokenHttpClient, authorizationStore, Options.Create(freeAgentOptions), Options.Create(authorizationOptions));

        using var apiHttpClient = new HttpClient();
        apiHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("InvoiceManager-IntegrationTests/1.0");
        var apiClient = new FreeAgentApiClient(
            apiHttpClient, tokenProvider, Options.Create(freeAgentOptions), NullLogger<FreeAgentApiClient>.Instance);
        var directory = new FreeAgentContactDirectory(apiClient);

        // Not asserting on specific contact data - the sandbox company's contents can change
        // freely and aren't this test's concern. What matters is that authentication succeeds and
        // FreeAgent accepts the request at all: a StubHttpMessageHandler-based unit test can
        // never catch a real API rejection like the missing User-Agent header FreeAgent requires
        // on every request, which broke every live FreeAgent call until manual testing surfaced
        // it - this test exists so a regression like that fails loudly for anyone who runs it
        // locally with real credentials, rather than only being found by hand again.
        var results = await directory.SearchAsync("a");

        Assert.NotNull(results);
    }
}
