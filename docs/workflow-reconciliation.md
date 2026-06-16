# Invoice Matching and OneDrive Reconciliation

This document describes how InvoiceManager should match expected invoices to
source invoices and to files that already exist in OneDrive.

## Purpose

InvoiceManager must be able to run safely every day without duplicating files or
losing track of invoices that were handled outside the service.

The workflow must account for:

- Invoices that exist in the source system but have not been saved to OneDrive.
- Invoices that appear several days away from their nominal expected date.
- Providers that expose limited metadata before the invoice PDF is opened.
- Multiple invoices from one provider for the same period.
- Files that already exist in OneDrive because of historical manual downloads,
  manual repairs, or previous partial runs.
- Database records that do not yet know about files already present in OneDrive.

## Daily Workflow

The daily controller should process each due or retryable expected invoice as
follows:

1. Load the expected invoice record and its configuration.
2. Build provider-independent invoice search criteria from the expected record.
3. Search the configured OneDrive destination for an existing matching file.
4. If a OneDrive match exists, update the invoice record as reconciled from
   OneDrive and continue the downstream workflow from that saved file.
5. If no OneDrive match exists, call the source integration with the invoice
   search criteria.
6. Validate that the retrieved invoice matches the expected invoice.
7. Save the retrieved invoice to OneDrive.
8. Continue with FreeAgent attachment behavior.
9. Create the next expected invoice record once the configured success state is
   reached.
10. Persist state and telemetry after each meaningful step.

FreeAgent behavior is intentionally left at a high level here and should be
expanded when the FreeAgent integration is designed in detail.

## Search Criteria

Expected invoice records should provide the criteria used to locate an invoice:

- Expected invoice date.
- Date tolerance or date window from stored data.
- Expected amount.
- Expected currency.
- Expected VAT mode where relevant.
- Invoice name or category for filenames and reporting.

Expected values are matching criteria. They should not be overwritten by actual
values found during retrieval or reconciliation.

## Source Matching

Source integrations should receive provider-independent search criteria and
translate them into provider-specific behavior.

The core workflow owns the decision about whether a retrieved source invoice is
acceptable. Source integrations should return the invoice file or file reference
plus actual metadata such as invoice date, amount, currency, source invoice ID,
and provider metadata.

For Microsoft 365, amount and invoice date must both match within the configured
tolerances. Microsoft 365 does not expose a stable product identifier before the
PDF contents are opened, so product labels such as Copilot or Office extensions
must not be required as source-system match keys. Those labels are still useful
for expected invoice identity, generated filenames, logs, and reporting.

Currency must be part of every amount comparison. For example, OpenAI invoices
may be in USD while most other invoices are expected to be in GBP.

## OneDrive Reconciliation

Before calling a source integration, the workflow should search the configured
OneDrive destination for a file that satisfies the expected invoice criteria.

If a matching file is found, the workflow should:

- Record the OneDrive location and file identifier where available.
- Mark the invoice as reconciled from OneDrive.
- Record when reconciliation occurred.
- Record the reason the match was accepted.
- Continue downstream processing from the existing OneDrive file.

OneDrive matching should use the same date, amount, and currency tolerances as
source matching. A match by amount and date is automatic when both values match
within the configured tolerances.

Reconciliation is normal workflow behavior, not an exceptional repair path.

## Ambiguity and Duplicates

The workflow should avoid creating duplicate OneDrive files and duplicate invoice
records.

If multiple candidate OneDrive files satisfy the same expected invoice criteria,
the workflow should still accept an automatic match using the configured
tolerances. The implementation should record enough match detail to explain
which file was selected.

The service should avoid creating more than one expected invoice record for the
same configuration and period. Next-record creation must be idempotent so a retry
or manual re-run can safely detect that the next expected record already exists.

## Status Transitions

Exact persisted status names should be finalized during implementation, but the
workflow should support these conceptual transitions:

```text
Expected
  -> ReconciledFromOneDrive
  -> UploadedToFreeAgent
  -> NextExpectedCreated

Expected
  -> Retrieved
  -> SavedToOneDrive
  -> UploadedToFreeAgent
  -> NextExpectedCreated

Expected
  -> NotFound
  -> Expected or retryable status on a later run

Any processing step
  -> Failed
  -> Retryable status on a later run
```

The workflow should persist after each meaningful step so retries can continue
without repeating completed work.

## Audit Trail

Records updated from existing OneDrive files should preserve that they were
reconciled from OneDrive, when that happened, and why the match was accepted.

The first implementation can keep this lightweight with fields on the invoice
record, such as `reconciliationSource`, `reconciledAt`, and `matchReason`.
Possible reconciliation sources include automatic OneDrive scans, future manual
override tooling, and future migration tooling.
