namespace InvoiceManager.Core.Integrations.FreeAgent;

/// <summary>The Guess was removed, the bank transaction verified unexplained, and the retried bill update succeeded.</summary>
public sealed record FreeAgentGuessRemoved(FreeAgentBillSnapshot Bill);

/// <summary>
/// One of the four preconditions (exactly one matching explanation, marked for
/// review, deletable, unlocked) no longer held when re-checked immediately before
/// deletion. Nothing was deleted.
/// </summary>
public sealed record FreeAgentGuessRevalidationFailed(string Reason);

/// <summary>
/// The Guess was deleted, but the bank-transaction-unexplained check or the single
/// retried bill update failed afterward. Remaining remote state is preserved - no
/// further automatic action is taken.
/// </summary>
public sealed record FreeAgentGuessRemovalRetryFailed(string Reason);

/// <summary>The outcome of a confirmed Guess-removal action.</summary>
public union FreeAgentGuessRemovalResult(
    FreeAgentGuessRemoved, FreeAgentGuessRevalidationFailed, FreeAgentGuessRemovalRetryFailed);

/// <summary>
/// Removes a confirmed unapproved Bill Payment Guess and retries the blocked bill
/// update. Deliberately a separate interface from <see cref="IFreeAgentBillReconciler"/>
/// and never referenced by the unattended due-invoice processing path - only the
/// (future) AdminWeb confirmed-removal action may call this, so the unattended
/// workflow cannot delete a Guess even by accident.
/// </summary>
public interface IFreeAgentGuessRemover
{
    Task<FreeAgentGuessRemovalResult> RemoveConfirmedGuessAsync(
        FreeAgentGuessIntervention intervention, CancellationToken cancellationToken = default);
}
