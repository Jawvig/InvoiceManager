# Product Overview

InvoiceManager collects company invoices from external systems, saves them to
the right OneDrive folders, and attaches them to the matching FreeAgent bills.
It is intended to run unattended, while still being safe to retry after partial
failures or manual repairs.

## Goals

- Retrieve expected invoices from configured sources.
- Save each invoice to its configured OneDrive destination.
- Generate consistent filenames from invoice date, description, total, currency,
  and VAT mode.
- Track expected, missing, retrieved, reconciled, saved, and attached invoices.
- Upload invoices to the relevant FreeAgent bills.
- Update FreeAgent bill totals where configuration allows and the invoice total
  is authoritative.
- Run from scheduled triggers, with an optional operational rerun trigger.

## Non-Goals

- Invoice generation.
- Customer billing.
- Payment collection.
- A replacement for accounting software.
- General-purpose document management outside the invoice workflow.

## Invoice Sources

Initial planned sources are:

- Microsoft Azure.
- Microsoft 365.
- OpenAI.
- Microsoft 365 mailbox attachments.

Each source should be implemented behind an integration interface so new sources
can be added without changing the core workflow.

Some providers expose limited metadata before the invoice document is opened. For
example, Microsoft 365 invoices may need to be matched by expected date, amount,
currency, and source invoice identifier where available. Product labels such as
`Copilot 365` are useful for configuration, reporting, and filenames, but should
not be required as source-system match keys unless the provider exposes them
reliably.

## Invoice Destinations

Retrieved invoices are saved to configured OneDrive folders. The saved filename
should include:

- ISO invoice date.
- Description of what the invoice is for.
- Invoice total and currency.
- VAT mode: inclusive (`inc`) or exclusive (`exc`).

InvoiceManager must also account for files that already exist in OneDrive. This
can happen after historical manual downloads, manual repairs, or retries after a
partial failure. The workflow should check OneDrive before retrieving a fresh
copy from the source system.

## Expected Invoice Lifecycle

The service maintains configuration for recurring invoice expectations. From that
configuration it creates expected invoice records with matching criteria such as
expected date, amount, currency, and date tolerance.

For each due or retryable expected invoice, the service should:

1. Check the configured OneDrive destination for an existing matching file.
2. Retrieve the invoice from the source integration if no OneDrive match exists.
3. Save newly retrieved invoices to OneDrive.
4. Attach the saved or reconciled invoice to the matching FreeAgent bill.
5. Create the next expected invoice record when the configured success state is
   reached.

Expected metadata should remain separate from actual metadata. Expected values
help locate the invoice; actual values record what was found.

## Failure Handling

The service should persist state after meaningful steps so a later run can retry
without losing progress or duplicating work.

Examples of failures to record:

- Expected invoice not found.
- Invoice found but OneDrive save failed.
- File saved but FreeAgent upload failed.
- FreeAgent bill total mismatch could not be resolved.
- External provider authentication failure.
