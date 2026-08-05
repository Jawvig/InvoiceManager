namespace InvoiceManager.Core;

/// <summary>
/// How to find and reconcile the FreeAgent bill for this invoice configuration's
/// invoices, if any. Absent for configurations that do not use FreeAgent - see
/// <see cref="InvoiceConfiguration.FreeAgentMatching"/>.
/// </summary>
/// <remarks>
/// There is no company URL here: FreeAgent bills are not scoped by company URL -
/// the connected OAuth token alone determines which company's bills are visible,
/// and the same fixed API host serves every company (see
/// <c>IFreeAgentTokenProvider</c>/<c>FreeAgentApiHost</c>).
/// </remarks>
public sealed record FreeAgentBillMatching(
    string ContactUrl,
    Option<FreeAgentDateReconciliation> DateReconciliation,
    Option<FreeAgentAmountReconciliation> AmountReconciliation);

/// <summary>
/// Opts a configuration into date reconciliation: bills are searched for within
/// <see cref="ToleranceDays"/> of the actual invoice date, and a matched bill whose
/// <c>dated_on</c> differs from the actual invoice date is corrected to match it.
/// Absent (see <see cref="FreeAgentBillMatching.DateReconciliation"/>) means only
/// exact-dated bills are matched, and no correction is ever attempted - matching a
/// bill loosely only to leave its date silently wrong forever is never useful.
/// </summary>
public sealed record FreeAgentDateReconciliation(int ToleranceDays);

/// <summary>
/// Opts a configuration into amount reconciliation: bills are matched with a total
/// within <see cref="AmountTolerance"/> of the actual invoice amount, and a matched
/// bill's single item is corrected to the actual amount when its total differs.
/// Absent (see <see cref="FreeAgentBillMatching.AmountReconciliation"/>) means only
/// exact-amount bills are matched, and no correction is ever attempted, for the same
/// reason as <see cref="FreeAgentDateReconciliation"/>.
/// </summary>
public sealed record FreeAgentAmountReconciliation(decimal AmountTolerance);
