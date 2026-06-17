# Architecture

InvoiceManager is planned as a C#/.NET service that runs unattended in Azure and
locally through Aspire.

## Runtime Model

The primary runtime is an Azure Functions isolated worker app.

Initial triggers:

- Timer trigger for scheduled invoice checks.
- Optional HTTP trigger for manual local or operational re-runs.

The timer-triggered workflow should:

1. Load invoice configuration from storage.
2. Find expected invoice records that are due or retryable.
3. For each expected invoice, check the configured OneDrive destination for an
   existing matching file.
4. If a matching OneDrive file exists, update the invoice record as reconciled
   and continue the downstream workflow from that saved file.
5. If no matching OneDrive file exists, ask the relevant invoice integration to
   retrieve an invoice using the expected invoice selection criteria.
6. Save retrieved invoice files to OneDrive.
7. Upload saved or reconciled invoices to FreeAgent bills.
8. Create the next expected invoice record when the current invoice reaches the
   configured success state.
9. Persist invoice state after each meaningful step.
10. Record logs, metrics, and failures.

## Azure Hosting

Production hosting should use consumption-oriented Azure services where
practical:

- Azure Functions Consumption Plan for the background service.
- Azure Cosmos DB for NoSQL in serverless mode for persistent storage.
- Azure Key Vault for secrets.
- Application Insights and Azure Monitor for observability.

## Local Development

Local development should use Aspire as the orchestration entry point.

The Aspire AppHost should eventually coordinate:

- The Azure Functions project.
- Local or emulator-backed development dependencies where practical.
- Configuration needed for local development.
- Observability views useful during development.

Aspire is currently intended for local development orchestration, not as the
primary production hosting model.

## Project Structure

The intended project layout is:

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

## Core Workflow

The core workflow should be provider-independent. It should decide:

- Which invoices are expected.
- Which integration should be used.
- Which expected date, amount, and currency should be passed to an invoice
  source integration or OneDrive integration as search criteria.
- How to interpret the match result returned by an integration.
- Where the invoice should be saved.
- Whether a FreeAgent bill needs to be created, updated, or attached to.
- How invoice state should transition after each step.
- When to create the next expected invoice record.

The core workflow should not know the details of Microsoft Graph, OpenAI billing
APIs, Azure billing APIs, OneDrive APIs, or FreeAgent APIs.

The core workflow should not reimplement provider-specific matching rules. It
passes criteria to the relevant integration and receives either no match, an
unambiguous accepted match, or a failure/diagnostic result that can be persisted
and logged.

The database is the workflow ledger, but it is not the only source of truth for
whether an invoice file has already been saved. OneDrive reconciliation is part
of the normal workflow so that pre-existing files, manually repaired files, and
retries after partial failures do not result in duplicate saved files.

## Integration Model

Invoice integrations should be plugin-like. Each integration should implement a
shared contract and be selected by configuration.

Conceptual responsibilities:

- Invoice source integrations search for and retrieve matching invoice files and
  metadata using provider-independent selection criteria supplied by the core
  workflow.
- OneDrive integration searches configured folders for existing matching files,
  returns match details, and saves files to configured folders.
- FreeAgent integration finds bills, updates totals when required, and uploads
  invoice attachments.

The exact C# interfaces will be defined during implementation, but they should
support provider-independent workflow testing.

The source integration contract should be able to accept search criteria such as
expected invoice date, date tolerance, expected amount and currency. The 
integration owns the provider-specific matching logic and should return either
no match or an accepted invoice match with invoice content or a retrievable file
reference plus actual metadata such as source invoice ID, invoice date, amount,
and currency.

The OneDrive integration contract should be able to search a configured
destination for an existing file that satisfies the expected invoice metadata and
return the saved location and match details when a match is found. The OneDrive
integration owns matching against OneDrive contents, including file listing,
metadata extraction, and any filename/content inspection needed to decide whether
a file satisfies the criteria.

## Storage

Persistent storage should use Azure Cosmos DB for NoSQL in serverless mode.

Storage should hold:

- Invoice configuration.
- Expected invoice records.
- Retrieved invoice records.
- Processing run records.

Cosmos DB should not be treated as a relational database. Model records around
the queries the service needs, especially:

- Find active invoice configurations.
- Find invoices expected on or before a date.
- Find invoice records by configuration.
- Find invoice records that need OneDrive reconciliation.
- Find failures that need retry.
- Find the latest completed invoice record for creating the next expected
  invoice.
- Review recent processing runs.

See [data-model.md](data-model.md) for the initial data model and
[workflow-reconciliation.md](workflow-reconciliation.md) for matching and
reconciliation behavior.

## Secrets

Secrets should be stored in Azure Key Vault.

Do not store secrets in:

- Source code.
- Documentation.
- Local committed configuration.
- Cosmos DB records.
- Test fixtures.

Local development should use safe local secret mechanisms and Aspire-supported
configuration where appropriate.

## Monitoring

Monitoring should use Application Insights and Azure Monitor.

The service should emit enough telemetry to answer:

- Which invoices were expected in a run?
- Which invoices were retrieved?
- Which invoices were missing?
- Which invoices were reconciled from existing OneDrive files?
- Which files were saved to OneDrive?
- Which FreeAgent bills were updated or attached to?
- Which external calls failed?
- Which invoice records need retry?

## Error Handling

The workflow should persist state after meaningful steps so a later retry can
continue safely.

Errors should be recorded with enough context to diagnose the failed provider,
invoice expectation, and processing step. Retrying should avoid duplicate file
saves or duplicate FreeAgent attachments where possible.

## Initial Decisions

- The primary implementation language is C#.
- Unit tests use xUnit.net.
- The hosted service uses Azure Functions isolated worker.
- Local orchestration uses Aspire.
- Persistent storage uses Azure Cosmos DB for NoSQL in serverless mode.
- Secrets use Azure Key Vault.
- Observability uses Application Insights and Azure Monitor.
