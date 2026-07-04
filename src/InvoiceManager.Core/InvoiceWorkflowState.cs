namespace InvoiceManager.Core;

/// <summary>
/// The invoice is expected but no attempt has yet been made to find it.
/// </summary>
public sealed record Expected;

/// <summary>
/// A retrieval attempt ran but the invoice was not available yet; later runs
/// should try again.
/// </summary>
public sealed record NotYetFound;

/// <summary>
/// The invoice could not be found within the configured tolerance window.
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
/// before the source integration retrieved a new copy.
/// </summary>
public sealed record ReconciledFromOneDrive(ActualInvoiceDetails ActualDetails, OneDriveDetails OneDriveDetails);

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
    NotYetFound,
    NotFound,
    RetrievalError,
    Retrieved,
    ReconciledFromOneDrive,
    SavedToOneDrive);
