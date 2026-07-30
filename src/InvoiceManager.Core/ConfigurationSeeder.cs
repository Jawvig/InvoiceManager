using InvoiceManager.Core.Repositories;

namespace InvoiceManager.Core;

/// <summary>
/// Seeds invoice configurations into the repository, skipping any that already exist.
///
/// <para>
/// Deliberately exempt from the duplicate-validation sentinel protocol described on
/// <see cref="InvoiceConfigurationService"/> (see also docs/data-model.md's "Duplicate-validation
/// sentinel" section and issue #92): it calls
/// <see cref="Repositories.IInvoiceConfigurationRepository.CreateIfNotExistsAsync"/> directly,
/// which never calls <see cref="InvoiceConfigurationValidation.ValidateNoDuplicateMatch"/> in the
/// first place, so there is no read-then-write duplicate-search-criteria check here for a
/// concurrent writer to race against - only an insert-if-absent by ID. It also only ever runs
/// single-threaded, once, at deploy time (see <c>tools/InvoiceManager.Seeder</c>), from a fixed,
/// hand-curated seed list, so there is no concurrent-caller scenario to protect against even if it
/// did perform that check. If seeding ever starts validating cross-configuration search criteria,
/// or ever runs concurrently with itself or with <see cref="InvoiceConfigurationService"/>'s
/// mutating methods, revisit this.
/// </para>
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
