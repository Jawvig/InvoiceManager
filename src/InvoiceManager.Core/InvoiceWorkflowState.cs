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
    SavedToOneDrive);
