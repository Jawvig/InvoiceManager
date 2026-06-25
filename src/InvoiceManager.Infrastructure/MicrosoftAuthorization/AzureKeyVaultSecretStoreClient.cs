using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

namespace InvoiceManager.Infrastructure.MicrosoftAuthorization;

public sealed class AzureKeyVaultSecretStoreClient : ISecretStoreClient
{
    private const string ObjectIsDeletedButRecoverableErrorCode = "ObjectIsDeletedButRecoverable";

    private readonly IAzureKeyVaultSecretClient secretClient;

    public AzureKeyVaultSecretStoreClient(Uri keyVaultUri)
        : this(new AzureKeyVaultSecretClient(new SecretClient(keyVaultUri, new DefaultAzureCredential())))
    {
    }

    internal AzureKeyVaultSecretStoreClient(IAzureKeyVaultSecretClient secretClient)
    {
        this.secretClient = secretClient;
    }

    public async Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            return await secretClient.GetSecretAsync(name, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task SetSecretAsync(
        string name,
        string value,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await secretClient.SetSecretAsync(name, value, cancellationToken);
        }
        catch (RequestFailedException ex) when (IsDeletedButRecoverableConflict(ex))
        {
            await RecoverDeletedSecretAsync(name, cancellationToken);
            await secretClient.SetSecretAsync(name, value, cancellationToken);
        }
    }

    public async Task DeleteSecretAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            await secretClient.DeleteSecretAsync(name, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
        }
    }

    private async Task RecoverDeletedSecretAsync(string name, CancellationToken cancellationToken)
    {
        await secretClient.RecoverDeletedSecretAsync(name, cancellationToken);
    }

    private static bool IsDeletedButRecoverableConflict(RequestFailedException exception)
    {
        if (exception.Status != 409)
        {
            return false;
        }

        return string.Equals(
                exception.ErrorCode,
                ObjectIsDeletedButRecoverableErrorCode,
                StringComparison.Ordinal) ||
            exception.Message.Contains(
                ObjectIsDeletedButRecoverableErrorCode,
                StringComparison.Ordinal) ||
            exception.Message.Contains(
                "deleted but recoverable",
                StringComparison.OrdinalIgnoreCase);
    }
}

internal interface IAzureKeyVaultSecretClient
{
    Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken);

    Task SetSecretAsync(string name, string value, CancellationToken cancellationToken);

    Task DeleteSecretAsync(string name, CancellationToken cancellationToken);

    Task RecoverDeletedSecretAsync(string name, CancellationToken cancellationToken);
}

internal sealed class AzureKeyVaultSecretClient : IAzureKeyVaultSecretClient
{
    private readonly SecretClient secretClient;

    public AzureKeyVaultSecretClient(SecretClient secretClient)
    {
        this.secretClient = secretClient;
    }

    public async Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken)
    {
        Response<KeyVaultSecret> response = await secretClient.GetSecretAsync(
            name,
            cancellationToken: cancellationToken);
        return response.Value.Value;
    }

    public async Task SetSecretAsync(string name, string value, CancellationToken cancellationToken)
    {
        await secretClient.SetSecretAsync(name, value, cancellationToken);
    }

    public async Task DeleteSecretAsync(string name, CancellationToken cancellationToken)
    {
        await secretClient.StartDeleteSecretAsync(name, cancellationToken);
    }

    public async Task RecoverDeletedSecretAsync(string name, CancellationToken cancellationToken)
    {
        RecoverDeletedSecretOperation operation = await secretClient.StartRecoverDeletedSecretAsync(
            name,
            cancellationToken);
        await operation.WaitForCompletionAsync(cancellationToken);
    }
}
