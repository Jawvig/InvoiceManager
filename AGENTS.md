# Agent Instructions

This file is the shared instruction entry point for coding agents working in
this repository, including Codex, Claude Code, and GitHub Copilot.

Before making implementation changes, read:

- [docs/product.md](docs/product.md)
- [docs/architecture.md](docs/architecture.md)
- [docs/domain-model.md](docs/domain-model.md)
- [docs/data-model.md](docs/data-model.md)
- [docs/deployment.md](docs/deployment.md) — deployment strategy, CI/CD pipeline, and infrastructure as code

## Project Intent

InvoiceManager is a C# service for retrieving company invoices from external
systems, saving them to OneDrive, and attaching them to FreeAgent bills. It is
intended to run unattended in Azure and locally through Aspire.

## Technical Direction

- Use C# wherever appropriate.
- Use Azure Functions isolated worker for the hosted background service.
- Use Aspire for local development orchestration.
- Use Azure Cosmos DB for NoSQL in serverless mode for persistent storage.
- Use Azure Key Vault for secrets.
- Use Application Insights and Azure Monitor for observability.
- Use xUnit.net for unit tests.

## Architecture Rules

- Keep provider-specific invoice retrieval logic behind integration interfaces.
- Do not put OpenAI, Microsoft 365, Azure, OneDrive, or FreeAgent-specific logic
  in the core workflow unless the architecture document explicitly allows it.
- Treat the core workflow as responsible for deciding what invoice is expected,
  whether it has been retrieved, where it should be saved, and what FreeAgent
  action is required.
- Keep integrations focused on external system behavior such as fetching an
  invoice, saving a file, or uploading an attachment.
- Do not hard-code secrets, credentials, tenant IDs, API keys, or personal data.
- Prefer strongly typed options and domain models over passing loose dictionaries
  through the application.
- Update the relevant documentation when changing architectural decisions,
  storage shape, integration behavior, or domain terminology.

## Domain-Sensitive Behavior

Be careful with:

- Invoice dates and ISO date formatting.
- Currency values and rounding.
- VAT inclusive (`inc`) and VAT exclusive (`exc`) totals.
- OneDrive folder destinations and generated filenames.
- FreeAgent bill totals and attachment URLs.
- Expected invoice frequency and missing-invoice detection.

## Testing Expectations

- Add or update xUnit.net tests for domain logic, workflow decisions, filename
  generation, schedule calculations, and provider-independent behavior.
- Prefer integration tests with fakes or test doubles before calling real
  external services.
- Do not require real secrets or paid cloud resources for unit tests.
