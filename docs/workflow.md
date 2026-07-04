# Invoice Matching and OneDrive Reconciliation

This document describes how InvoiceManager should use invoice search criteria to
find source invoices and files that already exist in OneDrive.

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
3. Ask the OneDrive integration to search the configured destination for an
   existing matching file.
4. If a OneDrive match exists, update the invoice record as reconciled from
   OneDrive and continue the downstream workflow from that saved file.
5. If no OneDrive match exists, call the source integration with the invoice
   search criteria.
6. If the source integration returns no match, record the invoice as not found
   or retryable according to the configured retry policy.
7. If the source integration returns an accepted match, use it as the retrieved
   invoice.
8. Save the retrieved invoice to OneDrive.
9. Continue with FreeAgent attachment behavior.
10. Create the next expected invoice record once the configured success state is
   reached.
11. Persist state and telemetry after each meaningful step.

FreeAgent behavior is intentionally left at a high level here and should be
expanded when the FreeAgent integration is designed in detail.

### Implementation status

The Microsoft 365 happy path is implemented: `DueInvoiceProcessor` loads due
records (steps 1–2), retrieves a match from the Microsoft 365 source integration
(steps 5, 7), saves the PDF to OneDrive (step 8), and creates the next expected
record (step 10), persisting after each step (`Expected → Retrieved →
SavedToOneDrive`). If a run fails after `Retrieved` and before
`SavedToOneDrive`, the next run treats that record as due again and resumes the
source retrieval/save path.

The not-found / retry states are also implemented (step 6). When the source
returns no match, the record moves to `NotYetFound` while today is before its
tolerance deadline (`expectedDate + dateToleranceDays`) and to the terminal
`NotFound` on or after that deadline — so a record first processed after its
window has elapsed goes straight to `NotFound`. A technical failure during
retrieval moves the record to `RetrievalError` (carrying the failure detail in
`lastError`). `ListDueAsync` picks up `Expected`, `NotYetFound`,
`RetrievalError`, and `Retrieved` records; `RetrievalError` is always retryable
with no retry limit. Each run emits structured telemetry — a per-record logging
scope (record, configuration, integration type) plus a run summary with saved /
not-yet-found / not-found / failed counts — captured by Application Insights.

OneDrive reconciliation (steps 3–4) and FreeAgent attachment (step 9) are
deferred to later work.

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
translate them into provider-specific search and matching behavior.

Source integrations own the decision about whether a source-system candidate
satisfies the supplied criteria. They should return either no match or an
accepted match with the invoice file or file reference plus actual metadata such
as invoice date, amount, currency, source invoice ID, and provider metadata. The
core workflow records and acts on that result; it does not duplicate the
provider-specific matching logic.

For Microsoft 365, amount and invoice date must both match within the configured
tolerances. Microsoft 365 does not expose a stable product identifier before the
PDF contents are opened, so product labels such as Copilot or Office extensions
must not be required as source-system match keys. Those labels are still useful
for expected invoice identity, generated filenames, logs, and reporting.

Currency must be part of every amount comparison. For example, OpenAI invoices
may be in USD while most other invoices are expected to be in GBP.

## OneDrive Reconciliation

Before calling a source integration, the workflow should ask the OneDrive
integration to search the configured OneDrive destination for a file that
satisfies the expected invoice criteria.

If a matching file is found, the workflow should:

- Record the OneDrive location and file identifier where available.
- Mark the invoice as reconciled from OneDrive.
- Record when reconciliation occurred.
- Record the reason the match was accepted.
- Continue downstream processing from the existing OneDrive file.

OneDrive matching should use the same date, amount, and currency tolerances as
source matching. A match by amount and date is automatic when both values match
within the configured tolerances.

The OneDrive integration owns matching against OneDrive contents. It should
return either no match or an accepted match with the OneDrive location, file
identifier where available, and match details that explain why the file was
accepted. The core workflow records the reconciliation result and continues the
downstream workflow from the matched file.

Reconciliation is normal workflow behavior, not an exceptional repair path.

## Ambiguity and Duplicates

The workflow should avoid creating duplicate OneDrive files and duplicate invoice
records.

If multiple candidate OneDrive files satisfy the same expected invoice criteria,
the OneDrive integration should still return an automatic match using the
configured tolerances. The implementation should record enough match detail to
explain which file was selected.

The service should avoid creating more than one expected invoice record for the
same configuration and period. Next-record creation must be idempotent so a retry
or manual re-run can safely detect that the next expected record already exists.

## Status Transitions

The implemented state machine for the `InvoiceWorkflowState` union — including the
`NotYetFound` / `NotFound` / `RetrievalError` retry states and the tolerance-deadline
rules — is drawn in [workflow-states.md](workflow-states.md).

The broader conceptual transitions (including the still-deferred FreeAgent and
next-expected steps) are:

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
