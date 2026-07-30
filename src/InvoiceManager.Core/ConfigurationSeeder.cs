using InvoiceManager.Core.Repositories;

namespace InvoiceManager.Core;

/// <summary>
/// Seeds invoice configurations into the repository, skipping any that already exist.
///
/// <para>
/// This class itself only ever runs single-threaded, once, at deploy time, from a fixed,
/// hand-curated seed list - but that alone does *not* exempt it from the duplicate-validation
/// sentinel protocol described on <see cref="InvoiceConfigurationService"/> (docs/data-model.md's
/// "Duplicate-validation sentinel" section, issue #92): per <c>scripts/Deploy-Infra.ps1</c>, a
/// deploy runs Terraform apply - which can start routing traffic to a live AdminWeb instance -
/// *before* invoking the seeder, so a seeded configuration can genuinely race a concurrent
/// Create/Update/Restore request coming through AdminWeb during that window, even though the
/// seeder itself never runs concurrently with another seeder call. <see cref="SeedAsync"/> never
/// calls <see cref="InvoiceConfigurationValidation.ValidateNoDuplicateMatch"/> directly (it's a
/// plain insert-if-absent by ID, not a read-then-write duplicate-search-criteria check), so
/// <see cref="Repositories.IInvoiceConfigurationRepository.CreateIfNotExistsAsync"/> itself carries
/// the sentinel participation instead of this class - see its XML doc: a successful insert
/// advances the sentinel atomically with it, so an AdminWeb request that read the sentinel before
/// the seeder's insert correctly loses its own write and revalidates.
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
