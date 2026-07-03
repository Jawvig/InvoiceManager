namespace InvoiceManager.Core;

/// <summary>
/// The due invoice was retrieved and saved to OneDrive, and the next expected
/// record was created.
/// </summary>
public sealed record ProcessingSucceeded(InvoiceRecordId RecordId);

/// <summary>
/// No source-system invoice matched the due record yet; the record is left in
/// its <see cref="Expected"/> state for a later run to retry.
/// </summary>
public sealed record ProcessingSkippedNoMatch(InvoiceRecordId RecordId);

/// <summary>
/// Processing the due record failed; other records in the same run are unaffected.
/// </summary>
public sealed record ProcessingFailed(InvoiceRecordId RecordId, Exception Exception);

/// <summary>The outcome of processing a single due invoice record.</summary>
public union DueInvoiceProcessingResult(ProcessingSucceeded, ProcessingSkippedNoMatch, ProcessingFailed);
