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

        if (await FindDuplicateMatchAsync(configuration, cancellationToken) is InvoiceConfigurationId conflictingId)
            return new DuplicateInvoiceConfigurationSearchCriteria(conflictingId);

        try
        {
            return await repository.CreateAsync(configuration, actor, cancellationToken);
        }
        catch (DuplicateInvoiceConfigurationException)
        {
            return new DuplicateInvoiceConfigurationId(configuration.Id);
        }
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

        if (await FindDuplicateMatchAsync(updated, cancellationToken) is InvoiceConfigurationId conflictingId)
            return new DuplicateInvoiceConfigurationSearchCriteria(conflictingId);

        try
        {
            return await repository.ReplaceAsync(
                updated, etag, InvoiceConfigurationRevisionAction.Updated, actor, cancellationToken);
        }
        catch (InvoiceConfigurationConflictException)
        {
            return new InvoiceConfigurationConflict();
        }
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

        try
        {
            return await repository.ReplaceAsync(
                updated,
                stored.ETag,
                isActive ? InvoiceConfigurationRevisionAction.Activated : InvoiceConfigurationRevisionAction.Deactivated,
                actor,
                cancellationToken);
        }
        catch (InvoiceConfigurationConflictException)
        {
            return new InvoiceConfigurationConflict();
        }
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
        // must be checked here too, not just on Create/Update.
        if (await FindDuplicateMatchAsync(restored, cancellationToken) is InvoiceConfigurationId conflictingId)
            return new DuplicateInvoiceConfigurationSearchCriteria(conflictingId);

        try
        {
            return await repository.ReplaceAsync(
                restored, current.ETag, InvoiceConfigurationRevisionAction.Restored, actor, cancellationToken);
        }
        catch (InvoiceConfigurationConflictException)
        {
            return new InvoiceConfigurationConflict();
        }
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
}
