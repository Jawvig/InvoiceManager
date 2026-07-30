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
/// the sentinel participation instead of this class - see its XML doc. That method checks whether
/// a configuration with this ID already exists *first*, and only for a genuinely new ID does it
/// revalidate search criteria against the live list on *every* insert attempt (not just the
/// first), which matters for both race orderings: if the seeder wins the sentinel race first, a
/// losing AdminWeb writer's own revalidation (in <see cref="InvoiceConfigurationService"/>) catches
/// the conflict; if an AdminWeb writer wins first - either before the seeder's very first attempt,
/// or between the seeder losing a sentinel race and its retry - the seeder's own revalidation must
/// catch it instead, since nothing else will. See <see cref="SeedConfigurationConflictException"/>
/// for what happens when it does, and why the existing-ID check runs first: an already-seeded
/// configuration's live search criteria can legitimately drift away from what the seed file
/// originally specified (an admin edits it after it's first seeded, freeing its original criteria
/// for some other configuration to claim), and re-seeding that same ID later must remain a
/// harmless no-op even though the seed file's original criteria might by then genuinely match a
/// different live configuration - it must never be misreported as a conflict.
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

/// <summary>
/// Thrown by <see cref="Repositories.IInvoiceConfigurationRepository.CreateIfNotExistsAsync"/> when
/// a not-yet-existing seed configuration's search criteria would duplicate a *different*
/// configuration already present in the container (see
/// <see cref="InvoiceConfigurationValidation.ValidateNoDuplicateMatch"/>).
///
/// <para>
/// This can only ever fire for a genuinely new configuration ID:
/// <see cref="Repositories.IInvoiceConfigurationRepository.CreateIfNotExistsAsync"/> checks whether
/// a configuration with the same ID already exists *before* running this check at all, and returns
/// as its long-standing, always-safe no-op if so - re-seeding an already-existing ID never reaches
/// this validation, regardless of how far that existing configuration's live search criteria may
/// have since drifted from what the seed file originally specified. That ordering matters: an
/// admin can edit a seeded configuration's search criteria after it's first seeded, freeing up the
/// seed file's *original* criteria for some other configuration to legitimately claim - re-running
/// the seeder for that same ID later (e.g. on a redeploy) must remain harmless even though the
/// seed file's original criteria might by then genuinely match that other configuration.
/// </para>
///
/// <para>
/// Deliberately an exception, not a return-type union: unlike <see cref="InvoiceConfigurationService"/>'s
/// mutating methods - a normal admin action through AdminWeb that must degrade gracefully to a
/// message the user can act on - <see cref="Repositories.IInvoiceConfigurationRepository.CreateIfNotExistsAsync"/>
/// only ever runs from <see cref="ConfigurationSeeder"/> at deploy time, invoked by
/// <c>tools/InvoiceManager.Seeder</c> as a one-shot process. A *new* seed configuration conflicting
/// with a live configuration means either the hand-curated seed data or the live configuration it
/// now conflicts with is wrong and needs a human to fix - unlike the "ID already exists" case (a
/// legitimate, expected outcome of re-running the idempotent seeder), there is no reasonable
/// automatic recovery, so this fails the deploy loudly (an unhandled exception here exits the
/// seeder process non-zero, which <c>scripts/Deploy-Infra.ps1</c> already treats as a failed
/// deploy) rather than silently skipping the conflicting entry or leaving two conflicting
/// configurations both stored.
/// </para>
/// </summary>
public sealed class SeedConfigurationConflictException(string message) : Exception(message);
