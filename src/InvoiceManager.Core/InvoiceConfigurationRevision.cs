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

public sealed class InvoiceConfigurationConflictException(string message) : Exception(message);

public sealed class DuplicateInvoiceConfigurationException(string message) : Exception(message);
