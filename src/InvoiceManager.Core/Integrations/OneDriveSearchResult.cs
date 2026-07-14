namespace InvoiceManager.Core.Integrations;

/// <summary>
/// No file already present in OneDrive satisfied the supplied criteria.
/// </summary>
public sealed record NoOneDriveMatch;

/// <summary>
/// A file already present in OneDrive satisfied the criteria. Carries where the
/// file lives, the actual values read from its filename (the same values the
/// source integration would have returned), and a human-readable reason the
/// match was accepted for the reconciliation audit trail.
/// </summary>
public sealed record OneDriveMatch(
    OneDriveDetails OneDriveDetails,
    ActualInvoiceDetails Details,
    string MatchReason);

/// <summary>The outcome of asking the OneDrive integration to reconcile against existing files.</summary>
public union OneDriveSearchResult(NoOneDriveMatch, OneDriveMatch);
