# Architecture

InvoiceManager is a C#/.NET service that runs unattended in Azure and locally
through Aspire.

## Runtime Model

The primary hosted runtime is an Azure Functions isolated worker app.

Initial triggers:

- Timer trigger for scheduled invoice checks.
- Optional HTTP trigger for manual local or operational reruns.

Local development should use Aspire as the orchestration entry point. Aspire is
for local coordination and observability, not the primary production hosting
model.

## Project Structure

```text
src/
  InvoiceManager.AppHost/
  InvoiceManager.Functions/
  InvoiceManager.Core/
  InvoiceManager.Infrastructure/
  InvoiceManager.Integrations.Azure/
  InvoiceManager.Integrations.Microsoft365/
  InvoiceManager.Integrations.OpenAI/
  InvoiceManager.Integrations.FreeAgent/

tests/
  InvoiceManager.Core.Tests/
  InvoiceManager.Functions.Tests/
  InvoiceManager.Infrastructure.Tests/
```

## Workflow

The scheduled workflow should:

1. Load active invoice configurations.
2. Find expected invoice records that are due or retryable.
3. Build provider-independent search criteria from the expected record.
4. Ask OneDrive whether the configured destination already contains a matching
   file.
5. If a OneDrive match exists, mark the record as reconciled and continue from
   that file.
6. Otherwise, ask the configured source integration to retrieve a matching
   invoice.
7. Save newly retrieved invoices to OneDrive.
8. Upload saved or reconciled invoices to FreeAgent bills.
9. Create the next expected invoice record once the configured success state is
   reached.
10. Persist state, logs, metrics, and failures after meaningful steps.

Conceptual status transitions:

```text
Expected -> ReconciledFromOneDrive -> UploadedToFreeAgent -> NextExpectedCreated
Expected -> Retrieved -> SavedToOneDrive -> UploadedToFreeAgent -> NextExpectedCreated
Expected -> NotFound -> retryable on a later run
Any processing step -> Failed -> retryable on a later run
```

The exact persisted status names should be finalized during implementation and
kept stable once data exists.

## Core Responsibilities

The core workflow is provider-independent. It decides:

- Which invoices are expected.
- Which integration should be used.
- Which search criteria should be passed to source and OneDrive integrations.
- How to interpret integration results.
- Where the invoice should be saved.
- Which FreeAgent action is required.
- How invoice state should transition.
- When to create the next expected invoice record.

The core workflow must not know the details of Microsoft Graph, OpenAI billing
APIs, Azure billing APIs, OneDrive APIs, or FreeAgent APIs.

## Integration Responsibilities

Integrations own external-system behavior:

- Source integrations search for and retrieve invoice files and metadata.
- The OneDrive integration searches configured folders for existing matching
  files and saves newly retrieved files.
- The FreeAgent integration finds bills, updates totals where allowed, and
  uploads invoice attachments.

Source and OneDrive integrations receive provider-independent search criteria,
including expected date, tolerance, amount, currency, and VAT mode where
relevant. They return either no match, an accepted match, or a failure result
with diagnostics that can be persisted and logged.

For Microsoft 365, invoice date and amount must both match within configured
tolerances unless a more reliable provider-specific identifier is available.
Currency must be included in every amount comparison.

If multiple OneDrive candidates satisfy the same criteria, the integration should
return the selected match with enough detail to explain why that file was chosen.

## Storage

Persistent storage uses Azure Cosmos DB for NoSQL in serverless mode. Cosmos DB
is the workflow ledger, but OneDrive remains a source of truth for whether an
invoice file already exists.

Storage should hold:

- Invoice configuration.
- Invoice records.
- Processing run records.

See [data-model.md](data-model.md) for the initial container shape and query
patterns.

## Hosting and Platform Services

Production should use consumption-oriented Azure services where practical:

- Azure Functions Consumption Plan for the background service.
- Azure Cosmos DB for NoSQL in serverless mode.
- Azure Key Vault for secrets.
- Application Insights and Azure Monitor for observability.

Secrets must not be stored in source code, documentation, committed local
configuration, Cosmos DB records, or test fixtures.

## Monitoring

Telemetry should answer:

- Which invoices were expected, retrieved, missing, or reconciled.
- Which files were saved to OneDrive.
- Which FreeAgent bills were updated or attached to.
- Which external calls failed.
- Which invoice records need retry.

Errors should include enough context to identify the provider, invoice
expectation, processing step, and retry state.
