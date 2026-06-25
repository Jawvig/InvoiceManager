using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using Microsoft.Extensions.Options;

namespace InvoiceManager.Infrastructure.Tests;

public sealed class KeyVaultMicrosoftAuthorizationStoreTests
{
    [Fact]
    public async Task SaveTokenCacheAsync_StoresBase64PayloadUnderConfiguredSecretName()
    {
        var secretStore = new FakeSecretStoreClient();
        var store = CreateStore(secretStore);

        await store.SaveTokenCacheAsync([1, 2, 3]);

        Assert.Equal("AQID", secretStore.Secrets["custom-cache-secret"]);
    }

    [Fact]
    public async Task ReadTokenCacheAsync_ReturnsDecodedPayload()
    {
        var secretStore = new FakeSecretStoreClient();
        secretStore.Secrets["custom-cache-secret"] = Convert.ToBase64String([4, 5, 6]);
        var store = CreateStore(secretStore);

        var tokenCache = await store.ReadTokenCacheAsync();

        Assert.Equal([4, 5, 6], tokenCache);
    }

    [Fact]
    public async Task HasTokenCacheAsync_ReturnsFalse_WhenSecretIsMissing()
    {
        var store = CreateStore(new FakeSecretStoreClient());

        Assert.False(await store.HasTokenCacheAsync());
    }

    [Fact]
    public async Task ClearTokenCacheAsync_DeletesSecret()
    {
        var secretStore = new FakeSecretStoreClient();
        secretStore.Secrets["custom-cache-secret"] = Convert.ToBase64String([7, 8, 9]);
        var store = CreateStore(secretStore);

        await store.ClearTokenCacheAsync();

        Assert.False(secretStore.Secrets.ContainsKey("custom-cache-secret"));
    }

    private static KeyVaultMicrosoftAuthorizationStore CreateStore(FakeSecretStoreClient secretStore)
    {
        return new KeyVaultMicrosoftAuthorizationStore(
            secretStore,
            Options.Create(new MicrosoftAuthorizationOptions
            {
                TokenCacheSecretName = "custom-cache-secret"
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
