using InvoiceManager.Infrastructure.FreeAgentAuthorization;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using Microsoft.Extensions.Options;

namespace InvoiceManager.Infrastructure.Tests;

public sealed class KeyVaultFreeAgentAuthorizationStoreTests
{
    [Fact]
    public async Task SaveRefreshTokenAsync_StoresPlainValueUnderConfiguredSecretName()
    {
        var secretStore = new FakeSecretStoreClient();
        var store = CreateStore(secretStore);

        await store.SaveRefreshTokenAsync("refresh-token-value");

        Assert.Equal("refresh-token-value", secretStore.Secrets["custom-freeagent-refresh-token"]);
    }

    [Fact]
    public async Task SaveRefreshTokenAsync_Throws_WhenValueIsBlank()
    {
        var store = CreateStore(new FakeSecretStoreClient());

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveRefreshTokenAsync("   "));
    }

    [Fact]
    public async Task ReadRefreshTokenAsync_ReturnsStoredValue()
    {
        var secretStore = new FakeSecretStoreClient();
        secretStore.Secrets["custom-freeagent-refresh-token"] = "stored-token";
        var store = CreateStore(secretStore);

        Assert.Equal("stored-token", await store.ReadRefreshTokenAsync());
    }

    [Fact]
    public async Task HasRefreshTokenAsync_ReturnsFalse_WhenSecretIsMissing()
    {
        var store = CreateStore(new FakeSecretStoreClient());

        Assert.False(await store.HasRefreshTokenAsync());
    }

    [Fact]
    public async Task HasRefreshTokenAsync_ReturnsTrue_WhenSecretIsPresent()
    {
        var secretStore = new FakeSecretStoreClient();
        secretStore.Secrets["custom-freeagent-refresh-token"] = "stored-token";
        var store = CreateStore(secretStore);

        Assert.True(await store.HasRefreshTokenAsync());
    }

    [Fact]
    public async Task ClearRefreshTokenAsync_DeletesSecret()
    {
        var secretStore = new FakeSecretStoreClient();
        secretStore.Secrets["custom-freeagent-refresh-token"] = "stored-token";
        var store = CreateStore(secretStore);

        await store.ClearRefreshTokenAsync();

        Assert.False(secretStore.Secrets.ContainsKey("custom-freeagent-refresh-token"));
    }

    private static KeyVaultFreeAgentAuthorizationStore CreateStore(FakeSecretStoreClient secretStore)
    {
        return new KeyVaultFreeAgentAuthorizationStore(
            secretStore,
            Options.Create(new FreeAgentAuthorizationOptions
            {
                RefreshTokenSecretName = "custom-freeagent-refresh-token"
            }));
    }

    private sealed class FakeSecretStoreClient : ISecretStoreClient
    {
        public Dictionary<string, string> Secrets { get; } = [];

        public Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken = default)
        {
            Secrets.TryGetValue(name, out var value);
            return Task.FromResult(value);
        }

        public Task SetSecretAsync(
            string name,
            string value,
            CancellationToken cancellationToken = default)
        {
            Secrets[name] = value;
            return Task.CompletedTask;
        }

        public Task DeleteSecretAsync(string name, CancellationToken cancellationToken = default)
        {
            Secrets.Remove(name);
            return Task.CompletedTask;
        }
    }
}
