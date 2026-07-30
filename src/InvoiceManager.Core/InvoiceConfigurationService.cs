using System.Diagnostics;
using InvoiceManager.Core.Repositories;

namespace InvoiceManager.Core;

/// <summary>
/// Validated application service for configuration administration. The mutating methods
/// (<see cref="CreateAsync"/>, <see cref="UpdateAsync"/>, <see cref="SetActiveAsync"/>,
/// <see cref="RestoreAsync"/>) return <see cref="InvoiceConfigurationMutationResult"/> rather than
/// throwing for any outcome a normal user action can trigger (validation failure, a duplicate ID or
/// search-criteria match, a concurrent edit) - callers switch over the result instead of guessing
/// which exception types to catch. They still throw <see cref="ArgumentException"/> for a
/// caller/API-contract violation that the UI never produces (e.g. changing an immutable field),
/// since those indicate a bug in the calling code rather than a result to present to the user.
///
/// <para>
/// Create/Update/Restore all revalidate <see cref="FindDuplicateMatchAsync"/> before writing -
/// list the live configurations, then check the candidate against them. Read-then-write like that
/// is a race between two concurrent calls affecting different configuration IDs: both can read the
/// same list, both pass validation, and both commit (their per-document ETags don't conflict with
/// each other), reintroducing the exact duplicate the check exists to prevent. <see cref="MutateWithDuplicateValidationAsync"/>
/// closes that race with an ETag-protected sentinel document (<see cref="ConfigurationValidationSentinel"/>,
/// in the same constant partition as configurations - see docs/data-model.md): read the sentinel,
/// validate, then include a conditional replace of the sentinel in the same transactional batch as
/// the write. One concurrent writer's batch succeeds and changes the sentinel's ETag; the other's
/// batch is rejected with a precondition failure on that operation
/// (<see cref="ValidationSentinelConflict"/>), and this service retries the whole
/// read-validate-write sequence exactly once against the now-current sentinel and configuration
/// list before giving up and reporting <see cref="InvoiceConfigurationConflict"/> - the same
/// "reload and re-apply" outcome as an ordinary per-document ETag conflict, since sustained
/// contention on the second attempt is not expected in single-admin-team usage and is better
/// surfaced to the user than retried indefinitely.
/// </para>
/// </summary>
public sealed class InvoiceConfigurationService(IInvoiceConfigurationRepository repository)
{
    public Task<IReadOnlyList<StoredInvoiceConfiguration>> ListAsync(CancellationToken cancellationToken = default) =>
        repository.ListAllAsync(cancellationToken);

    public Task<Option<StoredInvoiceConfiguration>> GetAsync(
        InvoiceConfigurationId id,
        IntegrationType integrationType,
        CancellationToken cancellationToken = default) =>
        repository.GetAsync(id, integrationType, cancellationToken);

    public Task<IReadOnlyList<InvoiceConfigurationRevision>> ListRevisionsAsync(
        InvoiceConfigurationId id,
        IntegrationType integrationType,
        CancellationToken cancellationToken = default) =>
        repository.ListRevisionsAsync(id, integrationType, cancellationToken);

    public async Task<InvoiceConfigurationMutationResult> CreateAsync(
        InvoiceConfiguration configuration,
        InvoiceConfigurationActor actor,
        CancellationToken cancellationToken = default)
    {
        if (configuration.IsActive)
            throw new ArgumentException("New configurations must be saved as inactive drafts.", nameof(configuration));

        var errors = InvoiceConfigurationValidation.Validate(configuration);
        if (errors.Count > 0)
            return new InvoiceConfigurationValidationFailed(errors);

        return await MutateWithDuplicateValidationAsync(
            configuration,
            (sentinel, ct) => repository.CreateAsync(configuration, actor, sentinel, ct),
            cancellationToken);
    }

    public async Task<InvoiceConfigurationMutationResult> UpdateAsync(
        InvoiceConfiguration original,
        InvoiceConfiguration updated,
        string etag,
        InvoiceConfigurationActor actor,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentity(original, updated);
        if (original.IsActive != updated.IsActive)
            throw new ArgumentException("Activation state must be changed through the separate activate/deactivate action.");

        var errors = InvoiceConfigurationValidation.Validate(updated);
        if (errors.Count > 0)
            return new InvoiceConfigurationValidationFailed(errors);

        return await MutateWithDuplicateValidationAsync(
            updated,
            (sentinel, ct) => repository.ReplaceAsync(
                updated, etag, InvoiceConfigurationRevisionAction.Updated, actor, sentinel, ct),
            cancellationToken);
    }

    public async Task<InvoiceConfigurationMutationResult> SetActiveAsync(
        StoredInvoiceConfiguration stored,
        bool isActive,
        InvoiceConfigurationActor actor,
        CancellationToken cancellationToken = default)
    {
        var updated = stored.Configuration with { IsActive = isActive };

        var errors = InvoiceConfigurationValidation.Validate(updated);
        if (errors.Count > 0)
            return new InvoiceConfigurationValidationFailed(errors);

        // Activating/deactivating doesn't touch search criteria, so it never revalidates
        // FindDuplicateMatchAsync and has no sentinel race to protect against - pass Option.None
        // so the repository skips the sentinel operation entirely for this write.
        return ToMutationResult(await repository.ReplaceAsync(
            updated,
            stored.ETag,
            isActive ? InvoiceConfigurationRevisionAction.Activated : InvoiceConfigurationRevisionAction.Deactivated,
            actor,
            Option.None,
            cancellationToken));
    }

    public async Task<InvoiceConfigurationMutationResult> RestoreAsync(
        StoredInvoiceConfiguration current,
        InvoiceConfigurationRevision revision,
        InvoiceConfigurationActor actor,
        CancellationToken cancellationToken = default)
    {
        if (current.Configuration.Id != revision.ConfigurationId ||
            current.Configuration.IntegrationType != revision.IntegrationType)
            throw new ArgumentException("The revision does not belong to this configuration.", nameof(revision));

        var restored = revision.Snapshot with
        {
            Id = current.Configuration.Id,
            IsActive = current.Configuration.IsActive,
        };

        var errors = InvoiceConfigurationValidation.Validate(restored);
        if (errors.Count > 0)
            return new InvoiceConfigurationValidationFailed(errors);

        // A restored revision can reintroduce search criteria now used by another configuration
        // (e.g. that other configuration was created after this revision was recorded), so this
        // must be checked here too, not just on Create/Update - and it's exactly as vulnerable to
        // the same concurrent-write race, so it goes through the same sentinel protocol.
        return await MutateWithDuplicateValidationAsync(
            restored,
            (sentinel, ct) => repository.ReplaceAsync(
                restored, current.ETag, InvoiceConfigurationRevisionAction.Restored, actor, sentinel, ct),
            cancellationToken);
    }

    private static void EnsureIdentity(InvoiceConfiguration original, InvoiceConfiguration updated)
    {
        if (original.Id != updated.Id)
            throw new ArgumentException("Invoice configuration ID is immutable.");
        if (original.IntegrationType != updated.IntegrationType)
            throw new ArgumentException("Integration type is immutable.");
    }

    private async Task<Option<InvoiceConfigurationId>> FindDuplicateMatchAsync(
        InvoiceConfiguration configuration, CancellationToken cancellationToken)
    {
        var others = (await repository.ListAllAsync(cancellationToken)).Select(x => x.Configuration).ToList();
        return InvoiceConfigurationValidation.ValidateNoDuplicateMatch(configuration, others);
    }

    /// <summary>
    /// Runs the read-validate-write sequence shared by Create/Update/Restore, protected against
    /// the concurrent-write race described in this class's remarks: read the sentinel, revalidate
    /// <paramref name="candidate"/> against the live list, then attempt <paramref name="writeAsync"/>
    /// with that sentinel. If the write reports it lost the sentinel race
    /// (<see cref="ValidationSentinelConflict"/>), the whole sequence is retried
    /// exactly once against a freshly re-read sentinel and configuration list; a second loss is
    /// reported as <see cref="InvoiceConfigurationConflict"/> rather than retried indefinitely.
    /// </summary>
    private async Task<InvoiceConfigurationMutationResult> MutateWithDuplicateValidationAsync(
        InvoiceConfiguration candidate,
        Func<ConfigurationValidationSentinel, CancellationToken, Task<InvoiceConfigurationWriteResult>> writeAsync,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 2;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var sentinel = await repository.GetValidationSentinelAsync(cancellationToken);

            if (await FindDuplicateMatchAsync(candidate, cancellationToken) is InvoiceConfigurationId conflictingId)
                return new DuplicateInvoiceConfigurationSearchCriteria(conflictingId);

            switch (await writeAsync(sentinel, cancellationToken))
            {
                case StoredInvoiceConfiguration stored:
                    return stored;
                case DuplicateInvoiceConfigurationId duplicateId:
                    return duplicateId;
                case InvoiceConfigurationConflict conflict:
                    return conflict;
                case ValidationSentinelConflict when attempt < maxAttempts:
                    continue; // Lost the race - loop to reread and revalidate once more.
                case ValidationSentinelConflict:
                    return new InvoiceConfigurationConflict();
            }
        }

        throw new UnreachableException(
            $"{nameof(MutateWithDuplicateValidationAsync)} must return from within its retry loop.");
    }

    private static InvoiceConfigurationMutationResult ToMutationResult(InvoiceConfigurationWriteResult result) =>
        result switch
        {
            StoredInvoiceConfiguration stored => stored,
            DuplicateInvoiceConfigurationId duplicateId => duplicateId,
            InvoiceConfigurationConflict conflict => conflict,
            ValidationSentinelConflict => throw new InvalidOperationException(
                "The repository reported a sentinel conflict for a write that passed Option<ConfigurationValidationSentinel>.None " +
                "- it should never attempt the sentinel operation in that case."),
        };
}
