using InvoiceManager.Core.Integrations.FreeAgent;
using NodaMoney;

namespace InvoiceManager.TestSupport;

public sealed class FakeFreeAgentBillReconciler : IFreeAgentBillReconciler
{
    public Func<FreeAgentBillIdentity, DateOnly, FreeAgentReconciliationResult>? DateReconciliation { get; set; }
    public Func<FreeAgentBillIdentity, FreeAgentBillItemIdentity, Money, FreeAgentReconciliationResult>? AmountReconciliation { get; set; }

    public Task<FreeAgentReconciliationResult> ReconcileDateAsync(
        FreeAgentBillIdentity bill, DateOnly newDatedOn, CancellationToken cancellationToken = default)
    {
        var result = DateReconciliation?.Invoke(bill, newDatedOn)
            ?? throw new InvalidOperationException("FakeFreeAgentBillReconciler.DateReconciliation was not configured.");
        return Task.FromResult(result);
    }

    public Task<FreeAgentReconciliationResult> ReconcileItemAmountAsync(
        FreeAgentBillIdentity bill,
        FreeAgentBillItemIdentity item,
        Money newTotalValue,
        CancellationToken cancellationToken = default)
    {
        var result = AmountReconciliation?.Invoke(bill, item, newTotalValue)
            ?? throw new InvalidOperationException("FakeFreeAgentBillReconciler.AmountReconciliation was not configured.");
        return Task.FromResult(result);
    }
}
