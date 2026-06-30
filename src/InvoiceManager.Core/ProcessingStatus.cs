namespace InvoiceManager.Core;

/// <summary>
/// The current state of an expected invoice record as it moves through
/// retrieval, reconciliation, save, and attachment steps.
/// </summary>
public enum ProcessingStatus
{
    Expected,
    NotYetFound,
    NotFound,
    RetrievalError,
    Retrieved,
    ReconciledFromOneDrive,
    SavedToOneDrive,
}
