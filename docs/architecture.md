# Architecture

InvoiceManager is planned as a C#/.NET service that runs unattended in Azure and
locally through Aspire.

## Runtime Model

The primary runtime is an Azure Functions isolated worker app.

Initial triggers:

- Timer trigger for scheduled invoice checks.
- Optional HTTP trigger for manual local or operational re-runs.

The timer-triggered workflow should coordinate the invoice processing loop
described in [workflow.md](workflow.md).

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
- The local admin website used to capture delegated Microsoft authorization.
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
  InvoiceManager.AdminWeb/
  InvoiceManager.Functions/
  InvoiceManager.Core/
  InvoiceManager.Infrastructure/
  InvoiceManager.Integrations.Azure/
  InvoiceManager.Integrations.Microsoft365/
  InvoiceManager.Integrations.OpenAI/
  InvoiceManager.Integrations.FreeAgent/

tests/
  InvoiceManager.AdminWeb.Tests/
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
- How to handle the match result returned by an integration.
- Where the invoice should be saved.
- Whether a FreeAgent bill needs to be found, updated, or attached to.
- How invoice state should transition after each step.
- When to create the next expected invoice record.

The core workflow should not know the details of Microsoft Graph, OpenAI billing
APIs, Azure billing APIs, OneDrive APIs, or FreeAgent APIs.

The core workflow should not reimplement provider-specific matching rules. See
[workflow.md#source-matching](workflow.md#source-matching)
and
[workflow.md#onedrive-reconciliation](workflow.md#onedrive-reconciliation)
for the matching contract between the workflow and integrations.

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

Matching criteria and accepted-match result details are owned by
[workflow.md](workflow.md).

## Storage

Persistent storage should use Azure Cosmos DB for NoSQL in serverless mode.

Storage should hold:

- Invoice configuration.
- Invoice processing records.
- Processing run records.

Cosmos DB should not be treated as a relational database. See
[data-model.md](data-model.md) for the initial containers, fields, partition
keys, and query patterns.

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

## Admin Website

The admin website is a local-first ASP.NET Core app for operational setup. Its
first responsibility is to capture delegated Microsoft authorization for the
Terraform-managed Entra app registration and store the resulting MSAL token
cache material in Azure Key Vault.

The admin website is not part of the provider-independent core workflow. It
should not own invoice matching, OneDrive reconciliation, invoice configuration
editing, or FreeAgent behavior. Those remain workflow and integration concerns.

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
