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
            FreeAgentReconciled reconciled => new FreeAgentGuessRemoved(reconciled.Bill),
            _ => new FreeAgentGuessRemovalRetryFailed(
                "The retried bill update did not succeed after removing the Guess; remaining state preserved."),
        };
    }
}
