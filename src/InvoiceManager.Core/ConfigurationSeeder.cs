using InvoiceManager.Core.Repositories;

namespace InvoiceManager.Core;

/// <summary>
/// Seeds invoice configurations into the repository, skipping any that already exist.
/// </summary>
public sealed class ConfigurationSeeder(IInvoiceConfigurationRepository repository)
{
    public async Task SeedAsync(
        IEnumerable<InvoiceConfiguration> configurations,
        CancellationToken cancellationToken = default)
    {
        foreach (var configuration in configurations)
        {
            await repository.CreateIfNotExistsAsync(configuration, cancellationToken);
        }
    }
}
