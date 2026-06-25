using Azure;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;

namespace InvoiceManager.Infrastructure.Tests;

public sealed class AzureKeyVaultSecretStoreClientTests
{
    [Fact]
    public async Task SetSecretAsync_RecoversDeletedSecretAndRetries_WhenSecretIsDeletedButRecoverable()
    {
        var keyVaultClient = new FakeAzureKeyVaultSecretClient
        {
            FirstSetException = new RequestFailedException(
                409,
                "Secret token-cache is currently in a deleted but recoverable state, and its name cannot be reused; in this state, the secret can only be recovered or purged.",
                "Conflict",
                innerException: null)
        };
        var storeClient = new AzureKeyVaultSecretStoreClient(keyVaultClient);

        await storeClient.SetSecretAsync("token-cache", "new-value");

        Assert.Contains("token-cache", keyVaultClient.RecoveredSecrets);
        Assert.Equal("new-value", keyVaultClient.Secrets["token-cache"]);
        Assert.Equal(2, keyVaultClient.SetAttempts);
    }

    [Fact]
    public async Task SetSecretAsync_DoesNotRecover_WhenConflictHasDifferentErrorCode()
    {
        var keyVaultClient = new FakeAzureKeyVaultSecretClient
        {
            FirstSetException = new RequestFailedException(
                409,
                "Different conflict.",
                "DifferentConflict",
                innerException: null)
        };
        var storeClient = new AzureKeyVaultSecretStoreClient(keyVaultClient);

        var exception = await Assert.ThrowsAsync<RequestFailedException>(
            () => storeClient.SetSecretAsync("token-cache", "new-value"));

        Assert.Equal("DifferentConflict", exception.ErrorCode);
        Assert.Empty(keyVaultClient.RecoveredSecrets);
        Assert.Equal(1, keyVaultClient.SetAttempts);
    }

    private sealed class FakeAzureKeyVaultSecretClient : IAzureKeyVaultSecretClient
    {
        public Dictionary<string, string> Secrets { get; } = [];

        public List<string> RecoveredSecrets { get; } = [];

        public RequestFailedException? FirstSetException { get; init; }

        public int SetAttempts { get; private set; }

        public Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken)
        {
            Secrets.TryGetValue(name, out var value);
            return Task.FromResult(value);
        }

        public Task SetSecretAsync(string name, string value, CancellationToken cancellationToken)
        {
            SetAttempts++;

            if (SetAttempts == 1 && FirstSetException is not null)
            {
                throw FirstSetException;
            }

            Secrets[name] = value;
            return Task.CompletedTask;
        }

        public Task DeleteSecretAsync(string name, CancellationToken cancellationToken)
        {
            Secrets.Remove(name);
            return Task.CompletedTask;
        }

        public Task RecoverDeletedSecretAsync(string name, CancellationToken cancellationToken)
        {
            RecoveredSecrets.Add(name);
            return Task.CompletedTask;
        }
    }
}
