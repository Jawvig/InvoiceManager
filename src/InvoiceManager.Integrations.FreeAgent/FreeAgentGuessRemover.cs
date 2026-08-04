using InvoiceManager.Core;
using InvoiceManager.Core.Integrations.FreeAgent;

namespace InvoiceManager.Integrations.FreeAgent;

/// <summary>
/// Removes a confirmed Guess and retries the blocked bill update. Only ever
/// invoked from the confirmed-intervention path (the future AdminWeb approval
/// action) - never from the unattended due-invoice processing path, which has
/// no reference to this interface at all.
/// </summary>
internal sealed class FreeAgentGuessRemover : IFreeAgentGuessRemover
{
    private readonly FreeAgentApiClient client;
    private readonly IFreeAgentBillReconciler reconciler;

    public FreeAgentGuessRemover(FreeAgentApiClient client, IFreeAgentBillReconciler reconciler)
    {
        this.client = client;
        this.reconciler = reconciler;
    }

    public async Task<FreeAgentGuessRemovalResult> RemoveConfirmedGuessAsync(
        FreeAgentGuessIntervention intervention, CancellationToken cancellationToken = default)
    {
        // The intervention itself must be Approved - this is the type-level enforcement point
        // for "never delete an unapproved Guess": a caller passing a Pending, Declined, or
        // Expired intervention (a bug in the future AdminWeb confirm-handler) is refused here
        // regardless of whether the remote preconditions below happen to hold.
        if (intervention.Status != FreeAgentGuessInterventionStatus.Approved)
            return new FreeAgentGuessRevalidationFailed("The intervention has not been approved.");

        // Re-check every precondition immediately before deleting, fresh - never reused from
        // when the intervention was first created, since time has passed and an administrator
        // confirmed asynchronously.
        var bankAccountUrls = await client.GetBankAccountUrlsAsync(cancellationToken);
        var lookup = await FreeAgentPaymentGuard.FindGuessForBillAsync(
            client, bankAccountUrls, intervention.Bill.BillUrl, cancellationToken);

        if (lookup.Outcome != GuessLookup.ExactlyOne || lookup.Explanation is not { } explanation)
            return new FreeAgentGuessRevalidationFailed("No single matching, marked-for-review explanation was found on re-check.");

        if (!string.Equals(explanation.Url, intervention.GuessExplanationUrl, StringComparison.Ordinal))
            return new FreeAgentGuessRevalidationFailed("The matching explanation's URL no longer matches the intervention.");

        if (!explanation.IsDeletable || explanation.IsLocked)
            return new FreeAgentGuessRevalidationFailed("The explanation is no longer deletable and unlocked.");

        try
        {
            await client.DeleteExplanationAsync(explanation.Url!, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new FreeAgentGuessRemovalRetryFailed($"Deleting the Guess explanation failed: {ex.Message}");
        }

        try
        {
            var transaction = await client.GetBankTransactionAsync(intervention.BankTransactionUrl, cancellationToken);
            var stillExplained = transaction.Explanations is { Count: > 0 };
            if (stillExplained)
            {
                return new FreeAgentGuessRemovalRetryFailed(
                    "The bank transaction is still explained after deleting the Guess; remaining state preserved.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new FreeAgentGuessRemovalRetryFailed($"Verifying the bank transaction failed: {ex.Message}");
        }

        // Retry the originally-blocked item amount update exactly once.
        var retryResult = await reconciler.ReconcileItemAmountAsync(
            intervention.Bill,
            intervention.Item,
            intervention.ProposedBillAmount,
            cancellationToken);

        return retryResult switch
        {
            // The reconciler verified the item itself reflects the requested amount, but the
            // actual goal is the bill's aggregate total matching what the intervention proposed -
            // verify that explicitly, same as the unattended reconciliation path does, rather
            // than assuming the two always move together (VAT/rounding could leave them apart).
            FreeAgentReconciled reconciled when reconciled.Bill.TotalValue.Amount == intervention.ProposedBillAmount.Amount =>
                new FreeAgentGuessRemoved(reconciled.Bill),
            FreeAgentReconciled => new FreeAgentGuessRemovalRetryFailed(
                "The retried bill update was accepted but the bill's aggregate total still does not match the proposed amount; remaining state preserved."),
            _ => new FreeAgentGuessRemovalRetryFailed(
                "The retried bill update did not succeed after removing the Guess; remaining state preserved."),
        };
    }
}
