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
- When a C# type is serialized to JSON and consumed by hand-written JavaScript
  (e.g. `wwwroot/js/*.js` fetching an AJAX handler's `JsonResult`), there is no
  compiler across that boundary. Renaming or reshaping such a type must be
  paired with a search of `wwwroot/js` for that endpoint's consumers in the
  same change, not left for a later bug report.
- When translating an external HTTP API's failures into domain outcomes
  (Microsoft Graph, FreeAgent, etc.), enumerate the failure modes explicitly
  instead of special-casing only the one you happened to hit first: "not
  found" (404) and "malformed/invalid input" (400) are both realistic for a
  caller-supplied ID and usually need the same treatment, distinct from
  auth failures (401/403) and transient server errors (429/5xx), which
  should still propagate as failures.

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
- The Cosmos emulator integration tests (`InvoiceManager.Infrastructure.IntegrationTests`,
  tagged `[Trait("Category", "Integration")]`) require the Dockerised Cosmos
  emulator and are **not run in CI** — the emulator is too slow/unreliable to warm
  up on hosted runners. Run them locally with Docker before pushing changes that
  touch persistence:
  `dotnet test tests/InvoiceManager.Infrastructure.IntegrationTests`.
  CI runs everything else via `dotnet test --filter "Category!=Integration"`.
- The Playwright suite (`InvoiceManager.AdminWeb.PlaywrightTests`) is also
  excluded from CI for the same reason, and additionally needs real,
  Graph-verifiable Microsoft 365 credentials/IDs (see
  `tools/dev-setup/Set-SeedEnvironment.ps1`) — it is the one suite most likely
  to pass locally for the author and still be silently broken for CI/other
  reviewers, so treat a green run of it as informative, not authoritative.
- When you make a previously-permissive code path stricter (e.g. adding a new
  server-side check, live verification call, or discovery-list membership
  requirement to a form submission), grep the test suites for existing tests
  that submit synthetic/fabricated data through that same path — they are
  the most likely thing to silently start failing, and won't show up in CI if
  they live in the Cosmos or Playwright integration projects above.
- Test/dev bootstrap code (module initializers, fixture setup) should fail
  fast with a clear, actionable message when a required *real* external value
  is missing, rather than silently substituting a synthetic placeholder. A
  silent default turns a one-line "set this environment variable" error into
  a confusing failure that only surfaces later, inside whatever external call
  actually needed the real value.
