using NodaMoney;

namespace InvoiceManager.Core.Integrations.FreeAgent;

/// <summary>
/// Why FreeAgent rejected a mutation as locked. Enumerated explicitly rather than
/// collapsed into one generic "locked" case, per this codebase's rule to enumerate
/// external failure modes rather than special-case only the first one encountered.
/// </summary>
public enum FreeAgentLockReason
{
    /// <summary>
    /// FreeAgent reported <c>cached_total_value</c>/<c>bill_items.total_value</c>
    /// as locked - the proven signal for an existing (approved or unapproved)
    /// Bill Payment explanation blocking an amount change.
    /// </summary>
    CachedTotalLocked,

    /// <summary>
    /// Reserved for an accounting-period lock. Not yet observed live - the sandbox
    /// never exposed a permitted lock-date range - so this must not be treated as
    /// proven behaviour until an integration test actually exercises it.
    /// </summary>
    PeriodLocked,

    /// <summary>A locked-field response FreeAgent returned for a reason this integration does not yet recognise.</summary>
    Unknown,
}

/// <summary>
/// Everything needed to create an administrator-facing intervention for removing a
/// blocking Bill Payment "Guess" explanation. Returned by the reconciler instead of
/// ever deleting anything itself - see <see cref="IFreeAgentGuessRemover"/>.
/// </summary>
public sealed record FreeAgentPaymentInterventionDetails(
    FreeAgentBillIdentity Bill,
    FreeAgentBillItemIdentity Item,
    string BankTransactionUrl,
    string GuessExplanationUrl,
    Money CurrentBillAmount,
    Money ProposedBillAmount,
    string Reason);

/// <summary>The bill's date or amount was changed and the result verified.</summary>
public sealed record FreeAgentReconciled(FreeAgentBillSnapshot Bill);

/// <summary>The supplied item does not belong to the supplied bill. Rejected before any mutation request was sent.</summary>
public sealed record FreeAgentItemNotOnBill;

/// <summary>FreeAgent rejected the mutation as locked, for a reason unrelated to a removable Guess.</summary>
public sealed record FreeAgentBillLocked(FreeAgentLockReason Reason);

/// <summary>
/// FreeAgent rejected the mutation because a genuine Bill Payment (approved or an
/// unapproved Guess) is attached. The unattended workflow must never act on this
/// itself - it must create an administrator intervention and stop.
/// </summary>
public sealed record FreeAgentPaymentInterventionRequired(FreeAgentPaymentInterventionDetails Intervention);

/// <summary>The mutation succeeded but the post-mutation verification read back an inconsistent or unexpected result.</summary>
public sealed record FreeAgentVerificationFailed(string Detail);

/// <summary>FreeAgent rejected the mutation for a reason not covered by a more specific case.</summary>
public sealed record FreeAgentRemoteRejected(string Detail);

/// <summary>The outcome of a FreeAgent bill date or amount reconciliation attempt.</summary>
public union FreeAgentReconciliationResult(
    FreeAgentReconciled,
    FreeAgentItemNotOnBill,
    FreeAgentBillLocked,
    FreeAgentPaymentInterventionRequired,
    FreeAgentVerificationFailed,
    FreeAgentRemoteRejected);

/// <summary>
/// Reconciles a FreeAgent bill's date and amount against a retrieved/reconciled
/// invoice's actual values. Never attempts to remove a blocking payment itself -
/// see <see cref="IFreeAgentGuessRemover"/> for the confirmed-removal path.
/// </summary>
public interface IFreeAgentBillReconciler
{
    Task<FreeAgentReconciliationResult> ReconcileDateAsync(
        FreeAgentBillIdentity bill, DateOnly newDatedOn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the given item's total value. The item's ownership by <paramref name="bill"/>
    /// is verified before any mutation request is sent - the caller must always supply an
    /// explicit item identity; this never guesses which item to change on a multi-item bill.
    /// </summary>
    Task<FreeAgentReconciliationResult> ReconcileItemAmountAsync(
        FreeAgentBillIdentity bill,
        FreeAgentBillItemIdentity item,
        Money newTotalValue,
        CancellationToken cancellationToken = default);
}
