# GitHub Copilot Instructions

InvoiceManager is a C# service for retrieving company invoices, saving them to
OneDrive, and attaching them to FreeAgent bills.

Follow the shared repository guidance in `AGENTS.md`.

Key expectations:

- Use C# and .NET patterns.
- Keep invoice integrations behind interfaces.
- Keep provider-specific logic out of the core workflow.
- Use Azure Functions isolated worker for the hosted service.
- Use Azure Cosmos DB for NoSQL in serverless mode for storage.
- Use Azure Key Vault for secrets.
- Use xUnit.net for tests.
- Update docs when changing architecture, storage shape, or domain terminology.
