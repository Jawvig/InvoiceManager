namespace InvoiceManager.Core;

public enum InvoiceConfigurationRevisionAction
{
    PreAuditBaseline,
    Created,
    Updated,
    Activated,
    Deactivated,
    Restored,
}

public sealed record InvoiceConfigurationActor(string ObjectId, string DisplayName);

public sealed record InvoiceConfigurationRevision(
    string RevisionId,
    InvoiceConfigurationId ConfigurationId,
    IntegrationType IntegrationType,
    InvoiceConfigurationRevisionAction Action,
    DateTimeOffset Timestamp,
    string? ActorObjectId,
    string ActorDisplayName,
    InvoiceConfiguration Snapshot);

public sealed record StoredInvoiceConfiguration(InvoiceConfiguration Configuration, string ETag);

/// <summary>
/// A handle on the cross-configuration duplicate-search-criteria sentinel document's current
/// ETag - see docs/data-model.md's "Duplicate-validation sentinel" section for the document
/// shape and <see cref="InvoiceConfigurationService"/>'s remarks for the protocol this
/// participates in. Callers read one of these before validating a candidate configuration against
/// the live list, then pass it back into <see cref="Repositories.IInvoiceConfigurationRepository.CreateAsync"/>/
/// <see cref="Repositories.IInvoiceConfigurationRepository.ReplaceAsync"/> so the repository can
/// include a conditional write of the sentinel in the same transactional batch as the
/// configuration + revision documents.
/// </summary>
public sealed record ConfigurationValidationSentinel(string ETag);

/// <summary>
/// The sentinel document's ETag no longer matched at write time: another
/// Create/Update/Restore committed first and changed it, so this write's earlier
/// duplicate-search-criteria validation ran against a list that may now be stale.
/// <see cref="InvoiceConfigurationService"/> catches this internally (it is not part of
/// <see cref="InvoiceConfigurationMutationResult"/>) and retries the validate-then-write sequence
/// once against the fresh sentinel and configuration list before giving up.
/// </summary>
public sealed record ValidationSentinelConflict;

/// <summary>
/// The outcome of a repository-level write that participates in the duplicate-validation sentinel
/// protocol (<see cref="Repositories.IInvoiceConfigurationRepository.CreateAsync"/>/
/// <see cref="Repositories.IInvoiceConfigurationRepository.ReplaceAsync"/>). Translated at the
/// Cosmos SDK boundary inside <c>CosmosInvoiceConfigurationRepository</c> itself, per
/// docs/coding-standards.md's "Translate at the external-library boundary" - no
/// <see cref="Exception"/> for any of these outcomes crosses the repository's public surface.
/// </summary>
public union InvoiceConfigurationWriteResult(
    StoredInvoiceConfiguration,
    DuplicateInvoiceConfigurationId,
    InvoiceConfigurationConflict,
    ValidationSentinelConflict);
