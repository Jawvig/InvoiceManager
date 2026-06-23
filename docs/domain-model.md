# Domain Model

This document defines shared InvoiceManager vocabulary. Storage fields and
container design live in [data-model.md](data-model.md).

## Invoice Configuration

A recurring invoice expectation. It identifies the source integration, invoice
name, expected schedule, matching defaults, OneDrive destination, FreeAgent
matching information, and whether the expectation is active.

## Integration

A provider-specific component that interacts with an external system, such as an
Azure invoice source, Microsoft 365 invoice source, OpenAI invoice source,
OneDrive destination, or FreeAgent bill destination.

## Integration Type

The configured identifier used to select the correct integration for an invoice
configuration. Example values may include `Azure`, `Microsoft365`, `OpenAI`, and
`Microsoft365Email`.

## Invoice Name

The human-readable name of the expected invoice, such as `ChatGPT Plus` or
`Copilot 365`. It is used for configuration identity, filenames, logs, and
status records.

For providers that do not expose product metadata before the PDF is opened, the
invoice name should not be treated as a source-system match key.

## Expected Frequency

The recurrence pattern for an expected invoice, such as monthly, annual, or a
custom schedule. The first implementation should only support the schedules
needed by configured invoices.

## Expected Invoice

A record representing an invoice that should exist by a particular date. It
contains expected metadata used as matching criteria and remains separate from
actual metadata collected after retrieval or reconciliation.

## Invoice Search Criteria

Provider-independent criteria passed from the core workflow to source and
OneDrive integrations. Criteria may include expected date, date tolerance,
expected amount, currency, VAT mode, source invoice identifier where available,
and invoice name for reporting or filenames.

## Invoice Match

The result returned after an integration applies search criteria to external
candidates. A match should indicate no match, an accepted match, or a failure
with diagnostics. Accepted matches include the metadata needed to update invoice
state and explain why the candidate was accepted.

## Retrieved Invoice

An invoice found by a source integration. Retrieved metadata may include invoice
date, total, currency, VAT mode, source integration, source identifier, provider
metadata, and file content or a retrievable file reference.

## Reconciled Invoice

An expected invoice matched to a file already present in OneDrive before a fresh
source retrieval occurs. Reconciliation should record the OneDrive location,
when reconciliation occurred, and why the file was accepted.

## Invoice Date

The date shown on the invoice. When used in filenames, it should be represented
in ISO format.

## Invoice Total

The monetary total from the invoice, including amount, currency, and whether the
amount is VAT inclusive (`inc`) or VAT exclusive (`exc`).

Currency is part of the value. Raw decimal amounts must not be compared without
currency.

## Money Amount

A strongly typed value representing amount and currency. The implementation
should prefer a widely used .NET money library, such as `NodaMoney`, while
wrapping library types where useful so persistence and workflow rules remain
stable.

## OneDrive Destination

The configured OneDrive folder where an invoice should be saved. Saved file
locations should be recorded so later runs can avoid duplicates and continue
downstream work.

## FreeAgent Bill

The FreeAgent bill that corresponds to a retrieved or reconciled invoice. The
service should store the bill URL after matching or upload. If allowed by
configuration, the FreeAgent integration may update a bill total so it matches
the invoice total.

## Processing Run

A single execution of the background service. It records timing, trigger type,
status, and summary counts for monitoring and diagnosis.

## Next Expected Invoice

The future expected invoice record created after the current invoice reaches the
configured success state. Creation must be idempotent so retries or reruns do
not create duplicates for the same configured period.
