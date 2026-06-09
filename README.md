# InvoiceManager

InvoiceManager is a C# service for collecting company invoices from the places
they appear, saving them to the right OneDrive folders, and attaching them to
the matching bills in FreeAgent.

The service is intended to run unattended from Azure, primarily as an Azure
Functions isolated worker app on a consumption plan. Local development should be
orchestrated with Aspire.

## Goals

- Retrieve invoices from integrations such as Microsoft Azure, Microsoft 365,
  OpenAI, and Microsoft 365 email attachments.
- Save invoices to OneDrive with filenames that include the invoice ISO date,
  invoice purpose, invoice total, and whether that total is inclusive (`inc`) or
  exclusive (`exc`) of VAT.
- Upload each invoice to the relevant FreeAgent bill.
- Update a FreeAgent bill total when needed so it matches the retrieved invoice.
- Track expected invoices, retrieved invoices, saved file locations, and
  FreeAgent bill URLs.

## Architecture

The planned architecture uses:

- C# and .NET.
- Azure Functions isolated worker for scheduled/background execution.
- Azure Cosmos DB for NoSQL in serverless mode for invoice configuration,
  expected invoice records, retrieved invoice records, and processing runs.
- Azure Key Vault for secrets.
- Application Insights and Azure Monitor for observability.
- Aspire for local orchestration.
- xUnit.net for unit tests.

See [docs/architecture.md](docs/architecture.md) for the detailed architecture.

## Documentation

- [AGENTS.md](AGENTS.md) - shared guidance for coding agents.
- [CLAUDE.md](CLAUDE.md) - Claude Code entry point.
- [.github/copilot-instructions.md](.github/copilot-instructions.md) - GitHub
  Copilot custom instructions.
- [docs/product.md](docs/product.md) - product goals and workflows.
- [docs/domain-model.md](docs/domain-model.md) - shared domain vocabulary.
- [docs/architecture.md](docs/architecture.md) - runtime, hosting, integration,
  storage, and monitoring design.
- [docs/data-model.md](docs/data-model.md) - initial Cosmos DB data model.
