using NodaMoney;

namespace InvoiceManager.Core.Integrations;

/// <summary>
/// Criteria for reconciling against a file already in OneDrive. Beyond the shared
/// date/amount/currency tolerances it also carries the expected
/// <see cref="InvoiceDescription"/>: because several configurations can share one
/// destination folder (Microsoft 365 Business Basic and Copilot both save to
/// <c>Bills/Microsoft 365</c>), matching on date and amount alone could reconcile a
/// record against a different subscription's file. The description is part of the
/// canonical filename (our own artifact, not a provider product label), so
/// requiring it to match distinguishes them without depending on source metadata.
/// When <see cref="InvoiceDescription"/> is empty, description matching is skipped,
/// so the destination folder must uniquely identify the expected invoice.
/// </summary>
public sealed record OneDriveSearchCriteria(
    DateOnly ExpectedDate,
    int DateToleranceDays,
    Option<AmountMatchingCriteria> AmountMatchingCriteria,
    string InvoiceDescription)
{
    /// <summary>
    /// Whether a candidate file with the given actual date, amount, and description
    /// satisfies these criteria: the shared date/amount/currency tolerances must
    /// hold and, when an invoice description is supplied, the description must
    /// match (case-insensitively).
    /// </summary>
    public bool Matches(DateOnly actualDate, Money actualAmount, string actualDescription) =>
        InvoiceMatching.DateAndOptionalAmountMatch(
            ExpectedDate, DateToleranceDays, AmountMatchingCriteria, actualDate, actualAmount)
        && (string.IsNullOrWhiteSpace(InvoiceDescription) ||
            string.Equals(actualDescription, InvoiceDescription, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The absolute number of days between a candidate's date and the expected date,
    /// used to prefer the closest candidate when several match.
    /// </summary>
    public int DateDistanceDays(DateOnly actualDate) =>
        InvoiceMatching.DateDistanceDays(ExpectedDate, actualDate);
}
