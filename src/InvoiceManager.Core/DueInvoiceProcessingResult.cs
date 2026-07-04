namespace InvoiceManager.Core;

/// <summary>
/// The due invoice was retrieved and saved to OneDrive, and the next expected
/// record was created.
/// </summary>
public sealed record ProcessingSucceeded(InvoiceRecordId RecordId);

/// <summary>
/// No source-system invoice matched the due record, but it is still within its
/// tolerance window; the record is set to <see cref="NotYetFound"/> for a later
/// run to retry.
/// </summary>
public sealed record ProcessingNotYetFound(InvoiceRecordId RecordId);

/// <summary>
/// No source-system invoice matched the due record and its tolerance window has
/// elapsed; the record is set to the terminal <see cref="NotFound"/> state.
/// </summary>
public sealed record ProcessingNotFound(InvoiceRecordId RecordId);

/// <summary>
/// Processing the due record failed with a technical error; the record is set to
/// <see cref="RetrievalError"/> for a later run to retry. Other records in the
/// same run are unaffected.
/// </summary>
public sealed record ProcessingFailed(InvoiceRecordId RecordId, Exception Exception);

/// <summary>The outcome of processing a single due invoice record.</summary>
public union DueInvoiceProcessingResult(
    ProcessingSucceeded,
    ProcessingNotYetFound,
    ProcessingNotFound,
    ProcessingFailed);
