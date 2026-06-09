# Domain Model

This document defines the shared vocabulary for InvoiceManager.

## Invoice Configuration

A configuration entry describing an invoice that the service expects to retrieve
on a recurring basis.

Expected fields include:

- Integration type.
- Invoice name.
- Expected frequency.
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

## Invoice Date

The date shown on the invoice. This date is used in the saved OneDrive filename
and should be represented in ISO format when included in filenames.

## Invoice Total

The monetary total from the invoice.

Invoice totals must preserve:

- Amount.
- Currency.
- Whether the amount is VAT inclusive (`inc`) or VAT exclusive (`exc`).

## OneDrive Destination

The configured OneDrive folder where a retrieved invoice should be saved.

The saved location should be stored after upload so later runs can avoid
duplicating files and can show where the invoice was saved.

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
- `SavedToOneDrive`
- `UploadedToFreeAgent`
- `Failed`
- `Skipped`

Exact status names should be finalized during implementation and kept stable
once persisted.
