namespace InvoiceManager.Core;

/// <summary>
/// How to find and reconcile the FreeAgent bill for this invoice configuration's
/// invoices, if any. Absent for configurations that do not use FreeAgent - see
/// <see cref="InvoiceConfiguration.FreeAgentMatching"/>.
/// </summary>
/// <param name="DateToleranceDays">
/// Deliberately separate from <see cref="InvoiceConfiguration.DateToleranceDays"/> -
/// FreeAgent bill matching tolerance is not necessarily the same tolerance used for
/// source-invoice retrieval.
/// </param>
/// <param name="AllowDateReconciliation">
/// Explicit per-configuration opt-in: whether reconciliation is permitted at all
/// for this bill category is an administrator decision, not something inferred
/// from "safe."
/// </param>
public sealed record FreeAgentBillMatching(
    string CompanyUrl,
    string ContactUrl,
    int DateToleranceDays,
    decimal AmountTolerance,
    bool AllowDateReconciliation,
    bool AllowAmountReconciliation);
