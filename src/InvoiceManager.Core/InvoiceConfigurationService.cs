using InvoiceManager.Core.Repositories;

namespace InvoiceManager.Core;

/// <summary>Validated application service for configuration administration.</summary>
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

    public async Task<StoredInvoiceConfiguration> CreateAsync(
        InvoiceConfiguration configuration,
        InvoiceConfigurationActor actor,
        CancellationToken cancellationToken = default)
    {
        EnsureValid(configuration);
        if (configuration.IsActive)
            throw new ArgumentException("New configurations must be saved as inactive drafts.", nameof(configuration));
        return await repository.CreateAsync(configuration, actor, cancellationToken);
    }

    public async Task<StoredInvoiceConfiguration> UpdateAsync(
        InvoiceConfiguration original,
        InvoiceConfiguration updated,
        string etag,
        InvoiceConfigurationActor actor,
        CancellationToken cancellationToken = default)
    {
        EnsureIdentity(original, updated);
        if (original.IsActive != updated.IsActive)
            throw new ArgumentException("Activation state must be changed through the separate activate/deactivate action.");
        EnsureValid(updated);
        return await repository.ReplaceAsync(
            updated, etag, InvoiceConfigurationRevisionAction.Updated, actor, cancellationToken);
    }

    public async Task<StoredInvoiceConfiguration> SetActiveAsync(
        StoredInvoiceConfiguration stored,
        bool isActive,
        InvoiceConfigurationActor actor,
        CancellationToken cancellationToken = default)
    {
        var updated = stored.Configuration with { IsActive = isActive };
        EnsureValid(updated);
        return await repository.ReplaceAsync(
            updated,
            stored.ETag,
            isActive ? InvoiceConfigurationRevisionAction.Activated : InvoiceConfigurationRevisionAction.Deactivated,
            actor,
            cancellationToken);
    }

    public async Task<StoredInvoiceConfiguration> RestoreAsync(
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
        EnsureValid(restored);
        return await repository.ReplaceAsync(
            restored, current.ETag, InvoiceConfigurationRevisionAction.Restored, actor, cancellationToken);
    }

    private static void EnsureIdentity(InvoiceConfiguration original, InvoiceConfiguration updated)
    {
        if (original.Id != updated.Id)
            throw new ArgumentException("Invoice configuration ID is immutable.");
        if (original.IntegrationType != updated.IntegrationType)
            throw new ArgumentException("Integration type is immutable.");
    }

    private static void EnsureValid(InvoiceConfiguration configuration)
    {
        var errors = InvoiceConfigurationValidation.Validate(configuration);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(configuration));
    }
}
