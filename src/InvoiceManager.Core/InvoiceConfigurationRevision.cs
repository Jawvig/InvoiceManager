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
/// Thrown by <see cref="Repositories.IInvoiceConfigurationRepository.ReplaceAsync"/> to signal an
/// ETag mismatch. <see cref="InvoiceConfigurationService"/> currently catches it and reports it
/// through <see cref="InvoiceConfigurationMutationResult"/>'s
/// <see cref="InvoiceConfigurationConflict"/> case instead - it must never be thrown or caught
/// anywhere else, and in particular must never reach an AdminWeb page handler. This is a known
/// gap against docs/coding-standards.md's "Exceptions are not control flow" (the translation
/// belongs in the repository itself, not in its one current caller) - tracked in
/// <see href="https://github.com/Jawvig/InvoiceManager/issues/93">issue #93</see>, not a
/// sanctioned pattern to replicate elsewhere.
/// </summary>
public sealed class InvoiceConfigurationConflictException(string message) : Exception(message);

/// <summary>
/// Thrown by <see cref="Repositories.IInvoiceConfigurationRepository.CreateAsync"/> to signal that
/// the configuration ID already exists. <see cref="InvoiceConfigurationService"/> currently catches
/// it and reports it through <see cref="InvoiceConfigurationMutationResult"/>'s
/// <see cref="DuplicateInvoiceConfigurationId"/> case instead - it must never be thrown or caught
/// anywhere else, and in particular must never reach an AdminWeb page handler. This is a known
/// gap against docs/coding-standards.md's "Exceptions are not control flow" (the translation
/// belongs in the repository itself, not in its one current caller) - tracked in
/// <see href="https://github.com/Jawvig/InvoiceManager/issues/93">issue #93</see>, not a
/// sanctioned pattern to replicate elsewhere.
/// </summary>
public sealed class DuplicateInvoiceConfigurationException(string message) : Exception(message);
