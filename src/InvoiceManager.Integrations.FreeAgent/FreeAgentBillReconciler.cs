using InvoiceManager.Core.Integrations.FreeAgent;
using NodaMoney;

namespace InvoiceManager.Integrations.FreeAgent;

/// <summary>
/// Reconciles a FreeAgent bill's date and amount. Verifies item ownership before
/// any mutation request, inspects payment explanations for context (never
/// deleting anything itself), and translates a locked-field 422 into either an
/// administrator intervention (a genuine Guess is present) or a generic locked
/// conflict.
/// </summary>
internal sealed class FreeAgentBillReconciler : IFreeAgentBillReconciler
{
    private readonly FreeAgentApiClient client;

    public FreeAgentBillReconciler(FreeAgentApiClient client)
    {
        this.client = client;
    }

    public async Task<FreeAgentReconciliationResult> ReconcileDateAsync(
        FreeAgentBillIdentity bill, DateOnly newDatedOn, CancellationToken cancellationToken = default)
    {
        var before = await client.GetBillAsync(bill.BillUrl, cancellationToken);
        var beforeItemUrls = (before.BillItems ?? []).Select(i => i.Url).ToHashSet(StringComparer.Ordinal);
        var beforeDueOn = before.DueOn;
        var beforeStatus = before.Status;

        var result = await client.PutBillDateAsync(bill.BillUrl, newDatedOn, cancellationToken);
        if (result.IsLocked)
        {
            // The POC only proved a payment-related lock for item-amount changes, not date
            // changes (a date lock is more plausibly a period lock, unproven either way) - so
            // this never runs the Guess-lookup detour used by the amount path below.
            return new FreeAgentBillLocked(FreeAgentLockReason.Unknown);
        }

        var after = await client.GetBillAsync(bill.BillUrl, cancellationToken);

        // Verify the proven invariant explicitly rather than trusting it: changing dated_on
        // must preserve item URLs and due_on/status.
        var afterItemUrls = (after.BillItems ?? []).Select(i => i.Url).ToHashSet(StringComparer.Ordinal);
        if (!afterItemUrls.SetEquals(beforeItemUrls) || after.DueOn != beforeDueOn || after.Status != beforeStatus)
        {
            return new FreeAgentVerificationFailed(
                "Changing dated_on unexpectedly altered bill item URLs, due_on, or status.");
        }

        var newDatedOnText = newDatedOn.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        if (!string.Equals(after.DatedOn, newDatedOnText, StringComparison.Ordinal))
        {
            return new FreeAgentVerificationFailed(
                "FreeAgent accepted the dated_on change but the bill's date does not reflect it.");
        }

        return new FreeAgentReconciled(FreeAgentBillMapping.ToSnapshot(after));
    }

    public async Task<FreeAgentReconciliationResult> ReconcileItemAmountAsync(
        FreeAgentBillIdentity bill,
        FreeAgentBillItemIdentity item,
        Money newTotalValue,
        CancellationToken cancellationToken = default)
    {
        var current = await client.GetBillAsync(bill.BillUrl, cancellationToken);
        var ownsItem = (current.BillItems ?? []).Any(i =>
            string.Equals(i.Url, item.ItemUrl, StringComparison.Ordinal));
        if (!ownsItem)
            return new FreeAgentItemNotOnBill();

        var result = await client.PutBillItemAmountAsync(bill.BillUrl, item.ItemUrl, newTotalValue.Amount, cancellationToken);
        if (result.IsLocked)
            return await ClassifyLockedAsync(bill, item, result.LockedFieldDetail!, newTotalValue, cancellationToken);

        if (result.Value is not { } updated)
            return new FreeAgentRemoteRejected("FreeAgent did not return an updated bill.");

        // Verify the specific item actually reflects the requested total - a 200 response
        // does not by itself guarantee FreeAgent applied the change.
        var updatedItem = (updated.BillItems ?? []).FirstOrDefault(i => string.Equals(i.Url, item.ItemUrl, StringComparison.Ordinal));
        if (updatedItem?.TotalValue is not { } updatedTotalText ||
            !decimal.TryParse(updatedTotalText, System.Globalization.CultureInfo.InvariantCulture, out var updatedTotal) ||
            updatedTotal != newTotalValue.Amount)
        {
            return new FreeAgentVerificationFailed(
                "FreeAgent accepted the item amount change but the item's total_value does not reflect it.");
        }

        // Accept FreeAgent's VAT/net/rounding values as-is - never recompute locally.
        return new FreeAgentReconciled(FreeAgentBillMapping.ToSnapshot(updated));
    }

    /// <summary>
    /// A locked-field 422 was returned. Inspects payment explanations for context: a genuine
    /// unapproved Guess produces an intervention (never deleted here); any other locked
    /// response is a generic conflict.
    /// </summary>
    private async Task<FreeAgentReconciliationResult> ClassifyLockedAsync(
        FreeAgentBillIdentity bill,
        FreeAgentBillItemIdentity item,
        string lockedDetail,
        Money proposedAmount,
        CancellationToken cancellationToken)
    {
        var bankAccountUrls = await client.GetBankAccountUrlsAsync(cancellationToken);
        var lookup = await FreeAgentPaymentGuard.FindGuessForBillAsync(client, bankAccountUrls, bill.BillUrl, cancellationToken);

        if (lookup.Outcome != GuessLookup.ExactlyOne || lookup.Explanation is not { } explanation)
            return new FreeAgentBillLocked(FreeAgentLockReason.CachedTotalLocked);

        var currentBill = await client.GetBillAsync(bill.BillUrl, cancellationToken);
        var currency = currentBill.Currency ?? proposedAmount.Currency.Code;
        var currentAmount = decimal.TryParse(currentBill.TotalValue, System.Globalization.CultureInfo.InvariantCulture, out var total)
            ? new Money(total, currency)
            : new Money(0m, currency);

        var details = new FreeAgentPaymentInterventionDetails(
            bill,
            item,
            explanation.BankTransaction ?? string.Empty,
            explanation.Url ?? string.Empty,
            currentAmount,
            proposedAmount,
            $"FreeAgent rejected the amount change ({lockedDetail}); an unapproved Guess payment explanation is attached.");

        return new FreeAgentPaymentInterventionRequired(details);
    }
}
