# Product Overview

InvoiceManager manages company invoices that arrive across several external
systems. It should retrieve expected invoices, save them into the correct
OneDrive folders, and upload them to the matching FreeAgent bills.

## Goals

- Obtain invoices from the places they currently exist.
- Save each invoice to the correct OneDrive location.
- Generate consistent invoice filenames.
- Track which invoices were expected and which were retrieved.
- Upload invoices to the relevant FreeAgent bills.
- Adjust FreeAgent bill totals when needed so they match the invoice total.
- Run unattended from either scheduled triggers or external triggers.

## Non-Goals

- Invoice generation.
- Customer billing.
- Payment collection.
- Full accounting replacement.
- Manual document management outside the invoice workflow.

## Invoice Sources

Initial planned invoice sources include:

- Microsoft Azure.
- Microsoft 365.
- OpenAI.
- Email attachments from Microsoft 365 mailboxes.

Each source should be implemented as an invoice integration. The product should
be able to add more integrations without changing the core workflow.

Some providers expose limited metadata before the invoice document is opened.
InvoiceManager must therefore support matching expected invoices by
provider-specific selection criteria while keeping those provider details out of
the core workflow.

The Microsoft 365 email attachment source is the extreme case of this: none of
sender, date window, or body text reveal the invoice date, total, or VAT mode,
so those fields are read from the PDF itself (via Azure AI Document
Intelligence) once a candidate email is matched. See
[workflow.md#source-matching](workflow.md#source-matching).

Microsoft 365 may produce more than one invoice for the same period, such as
separate invoices for Copilot and Office 365 extensions. These should be modeled
as separate expected invoices even when they use the same source integration. See
[workflow.md#source-matching](workflow.md#source-matching)
for the matching rules used when a provider does not expose a stable product
identifier before the PDF is opened.

## Invoice Destinations

Retrieved invoices should be saved to configured OneDrive folders. The saved
filename should include:

- The ISO date of the invoice.
- A description of what the invoice is for.
- The invoice total.
- Whether the total is VAT inclusive (`inc`) or VAT exclusive (`exc`).

The exact filename format should be treated as domain logic and covered by
tests once implemented.

## Expected Invoice Workflow

InvoiceManager should maintain configuration for expected invoices. Each
configuration entry should include:

- Integration type.
- Invoice name.
- Expected frequency.
- OneDrive destination.
- FreeAgent matching information.

The service should use this configuration to determine which invoice is expected
next, track whether that invoice has been found, and continue the recurring
sequence after successful processing.

See [Domain Model: Expected Invoice](domain-model.md#expected-invoice) for the
shared vocabulary, [data-model.md](data-model.md) for persisted fields, and
[workflow.md](workflow.md) for matching,
reconciliation, retry, and next-record behavior.

## FreeAgent Workflow

After an invoice is available in OneDrive, either from retrieval or
reconciliation, the service should upload it to the relevant FreeAgent bill.

If the existing bill total does not match the invoice total, the
service may need to update the FreeAgent bill total before or during attachment.
This behavior should be explicit, logged, and testable.

## Failure Handling

The service should be able to record failures without losing invoice state.
Examples include:

- Expected invoice not found.
- Invoice found but file save failed.
- File saved but FreeAgent upload failed.
- FreeAgent bill found but total mismatch could not be resolved.
- External provider authentication failure.

Failures should be observable through logs and monitoring, and the persisted
invoice state should support retry.
