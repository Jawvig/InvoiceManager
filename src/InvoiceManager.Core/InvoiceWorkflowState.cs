using InvoiceManager.Core.Integrations.FreeAgent;

namespace InvoiceManager.Core;

/// <summary>
/// The invoice is expected and still due for retrieval. Covers both records that
/// have never been attempted and records whose retrieval attempts have so far
/// found no match while still inside the tolerance window; later runs retry.
/// </summary>
public sealed record Expected;

/// <summary>
/// The invoice could not be found on or after the configured tolerance deadline.
/// </summary>
public sealed record NotFound;

/// <summary>
/// A retrieval attempt failed with a technical error, so the system could not
/// determine whether the invoice exists. <see cref="ErrorMessage"/> captures the
/// failure for diagnosis. Later runs always retry, with no retry limit.
/// </summary>
public sealed record RetrievalError(string ErrorMessage);

/// <summary>
/// The invoice has been found by an integration and its actual values read.
/// </summary>
public sealed record Retrieved(ActualInvoiceDetails ActualDetails);

/// <summary>
/// The expected invoice was matched to a file already present in OneDrive
/// before the source integration retrieved a new copy. <see cref="MatchReason"/>
/// records why the file was accepted and <see cref="ReconciledAt"/> when it
/// happened, preserving the reconciliation audit trail.
/// </summary>
public sealed record ReconciledFromOneDrive(
    ActualInvoiceDetails ActualDetails,
    OneDriveDetails OneDriveDetails,
    string MatchReason,
    DateTimeOffset ReconciledAt);

/// <summary>
/// The retrieved invoice file has been saved to its OneDrive destination.
/// </summary>
public sealed record SavedToOneDrive(ActualInvoiceDetails ActualDetails, OneDriveDetails OneDriveDetails);

/// <summary>
/// The retrieved/reconciled invoice has been matched to a FreeAgent bill, but no
/// reconciliation or attachment has happened yet for this run.
/// </summary>
public sealed record FreeAgentBillMatched(
    ActualInvoiceDetails ActualDetails, OneDriveDetails OneDriveDetails, FreeAgentBillIdentity Bill);

/// <summary>
/// The FreeAgent bill's date and/or amount have been reconciled and verified. Only
/// the bill identity is persisted - the full verified snapshot is transient
/// workflow-step output (logged at the time), not durable state; a later step
/// re-reads the bill fresh rather than trusting a stored snapshot to still be current.
/// </summary>
public sealed record FreeAgentBillReconciled(
    ActualInvoiceDetails ActualDetails, OneDriveDetails OneDriveDetails, FreeAgentBillIdentity Bill);

/// <summary>
/// The invoice PDF has been uploaded/replaced on the matched FreeAgent bill and its
/// attachment metadata verified. Terminal success state for records whose
/// configuration uses FreeAgent.
/// </summary>
public sealed record FreeAgentAttached(
    ActualInvoiceDetails ActualDetails,
    OneDriveDetails OneDriveDetails,
    FreeAgentBillIdentity Bill,
    FreeAgentAttachmentMetadata Attachment);

/// <summary>
/// FreeAgent processing could not continue automatically and needs an
/// administrator decision (a Guess-removal intervention). The record stays here -
/// not retried automatically - until a decision is recorded against
/// <see cref="InterventionId"/> and a later run re-attempts reconciliation.
/// </summary>
public sealed record FreeAgentInterventionPending(
    ActualInvoiceDetails ActualDetails, OneDriveDetails OneDriveDetails, FreeAgentInterventionId InterventionId);

/// <summary>
/// A FreeAgent step failed technically, hit a lock/conflict, or returned a
/// business-rule rejection that isn't a normal match/no-match outcome. Always
/// retried on a later run, mirroring <see cref="RetrievalError"/>.
/// </summary>
public sealed record FreeAgentError(
    ActualInvoiceDetails ActualDetails, OneDriveDetails OneDriveDetails, string ErrorMessage);

/// <summary>
/// The current state of an invoice record as it moves through retrieval,
/// reconciliation, and save steps. Each case carries exactly the data valid
/// in that state, so a record cannot exist in a state without the values that
/// state requires.
/// </summary>
public union InvoiceWorkflowState(
    Expected,
    NotFound,
    RetrievalError,
    Retrieved,
    ReconciledFromOneDrive,
    SavedToOneDrive,
    FreeAgentBillMatched,
    FreeAgentBillReconciled,
    FreeAgentAttached,
    FreeAgentInterventionPending,
    FreeAgentError);
