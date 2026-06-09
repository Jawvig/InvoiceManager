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
2. Determine which invoices are expected.
3. Ask the relevant invoice integration to retrieve each expected invoice.
4. Save retrieved invoice files to OneDrive.
5. Upload saved invoices to FreeAgent bills.
6. Persist the resulting invoice state.
7. Record logs, metrics, and failures.

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
- Whether a retrieved invoice matches the expectation.
- Where the invoice should be saved.
- Whether a FreeAgent bill needs to be created, updated, or attached to.
- How invoice state should transition after each step.

The core workflow should not know the details of Microsoft Graph, OpenAI billing
APIs, Azure billing APIs, OneDrive APIs, or FreeAgent APIs.

## Integration Model

Invoice integrations should be plugin-like. Each integration should implement a
shared contract and be selected by configuration.

Conceptual responsibilities:

- Invoice source integrations retrieve invoice files and metadata.
- OneDrive integration saves files to configured folders.
- FreeAgent integration finds bills, updates totals when required, and uploads
  invoice attachments.

The exact C# interfaces will be defined during implementation, but they should
support provider-independent workflow testing.

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
- Find failures that need retry.
- Review recent processing runs.

See [data-model.md](data-model.md) for the initial data model.

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
