# Domain Model

This document defines the shared vocabulary for InvoiceManager.

## Invoice Configuration

A configuration entry describing an invoice that the service expects to retrieve
on a recurring basis.

Expected fields include:

- Integration type.
- Invoice name.
- Expected frequency.
- Expected amount and currency when needed for matching.
- OneDrive destination.
- FreeAgent matching information.
- Active/inactive state.

## Integration

A provider-specific component that interacts with an external system.

Examples:

- Microsoft Azure invoice source.
- Microsoft 365 invoice source.
- Microsoft 365 email attachment source.
- OpenAI invoice source.
- OneDrive file destination.
- FreeAgent bill destination.

## Integration Type

The configured identifier used to select the correct integration for an invoice
configuration.

Examples:

- `Azure`
- `Microsoft365`
- `OpenAI`
- `Microsoft365Email`

Exact values should be defined in code when implementation starts.

## Invoice Name

The human-readable name of the expected invoice.

Examples:

- `ChatGPT Plus`
- `Copilot 365`

The invoice name may be used in generated filenames, logs, and user-facing
status records.

For providers such as Microsoft 365, more than one expected invoice may share
the same integration type and period. In that case the invoice name identifies
the configured expectation and the generated filename, while date and amount are
used to match the source invoice before the PDF contents are available.

## Expected Frequency

The recurrence pattern for an expected invoice.

Examples may include:

- Monthly.
- Annual.
- Custom schedule.

The first implementation should only support frequencies that are needed by the
configured invoices.

## Expected Invoice

A record representing an invoice that should exist by a particular date.

Expected invoice records allow the service to detect missing invoices, retry
failed retrievals, and keep history separate from configuration.

An expected invoice can include selection criteria used to find the matching
source invoice or an existing OneDrive file:

- Expected invoice date.
- Expected amount.
- Expected currency.
- Date tolerance or matching window from configuration or stored data.
- Invoice name or category for filenames and reporting.

Expected metadata should be preserved separately from actual metadata collected
after the invoice is retrieved or reconciled.

## Invoice Search Criteria

The provider-independent criteria passed from the core workflow to an invoice
source integration when requesting an invoice.

Search criteria may include expected date, date tolerance, expected amount and
currency.
Source integrations can translate those criteria into provider-specific 
queries or UI navigation without exposing those details to the core workflow.

## Retrieved Invoice

An invoice that has been found by an integration.

Retrieved invoice metadata may include:

- Invoice date.
- Invoice total.
- Currency.
- VAT inclusive/exclusive indicator.
- Source integration.
- Source identifier.
- File content or file reference.

## Reconciled Invoice

An expected invoice that has been matched to a file already present in OneDrive
before the source integration retrieves a new copy.

Reconciliation allows the workflow to account for files that were downloaded
manually, uploaded as a manual repair, or saved by a previous partial run. A
reconciled invoice should record the OneDrive location, when reconciliation
occurred, and the reason the match was accepted.

Reconciled invoices should continue through the downstream workflow where
appropriate, including FreeAgent attachment once that integration behavior is
defined in detail.

## Invoice Match

The result of comparing an expected invoice with a candidate source invoice or
OneDrive file.

For Microsoft 365, amount and invoice date must both match within the configured
tolerances. The source does not expose a stable product identifier before the PDF
is opened, so product or category labels should not be required as source-system
match keys.

## Invoice Date

The date shown on the invoice. This date is used in the saved OneDrive filename
and should be represented in ISO format when included in filenames.

## Invoice Total

The monetary total from the invoice.

Invoice totals must preserve:

- Amount.
- Currency.
- Whether the amount is VAT inclusive (`inc`) or VAT exclusive (`exc`).

Currency must be part of all amount comparisons. For example, OpenAI invoices
may be in USD while most other invoices are expected to be in GBP, so raw decimal
amounts must not be compared without currency.

## Money Amount

A strongly typed value representing an amount and currency.

The implementation should prefer a widely used .NET money library rather than
passing loose decimal and string pairs through the application. `NodaMoney` is a
candidate package because it provides money and currency types backed by ISO
4217 currency data. The domain should still wrap library types where useful so
persistence and workflow rules remain stable if the implementation package
changes.

## OneDrive Destination

The configured OneDrive folder where a retrieved invoice should be saved.

The saved location should be stored after upload so later runs can avoid
duplicating files and can show where the invoice was saved.

OneDrive may already contain files that satisfy expected invoice records. The
workflow should treat OneDrive reconciliation as normal behavior rather than an
exception path.

## FreeAgent Bill

The FreeAgent bill that corresponds to a retrieved invoice.

The service should store the FreeAgent bill URL after matching or upload. If the
bill total does not match the invoice total, the service may update the bill
where the integration and configuration allow it.

## Processing Run

A single execution of the background service.

A processing run should record start time, finish time, status, and summary
counts so failures can be inspected through storage and monitoring.

## Processing Status

The current state of an expected or retrieved invoice.

Initial statuses may include:

- `Expected`
- `NotFound`
- `Retrieved`
- `ReconciledFromOneDrive`
- `SavedToOneDrive`
- `UploadedToFreeAgent`
- `NextExpectedCreated`
- `Failed`
- `Skipped`

Exact status names should be finalized during implementation and kept stable
once persisted.

## Next Expected Invoice

The future expected invoice record created after the current invoice reaches the
configured success state.

The next expected invoice is derived from the invoice configuration, recurrence
rule, and completed invoice metadata. Creation should be idempotent so retries
or repeated runs do not create duplicate records for the same configured period.
