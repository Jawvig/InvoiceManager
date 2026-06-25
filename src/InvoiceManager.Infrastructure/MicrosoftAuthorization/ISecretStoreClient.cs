namespace InvoiceManager.Infrastructure.MicrosoftAuthorization;

public interface ISecretStoreClient
{
    Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken = default);

    Task SetSecretAsync(string name, string value, CancellationToken cancellationToken = default);

    Task DeleteSecretAsync(string name, CancellationToken cancellationToken = default);
}
