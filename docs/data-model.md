# Data Model

InvoiceManager should use Azure Cosmos DB for NoSQL in serverless mode as the
initial persistent storage option.

The data model should be optimized for the service's operational queries rather
than normalized like a relational database.

## Containers

Initial containers:

- `invoice-configurations`
- `invoice-records`
- `processing-runs`

This can be adjusted during implementation if query patterns require fewer or
more containers.

## invoice-configurations

Stores recurring invoice expectations and provider configuration references.

Purpose:

- List active invoice configurations.
- Select the correct invoice integration.
- Determine expected invoice frequency.
- Determine OneDrive and FreeAgent behavior.

Suggested partition key:

- `/integrationType`

Candidate fields:

- `id`
- `integrationType`
- `invoiceName`
- `expectedFrequency`
- `isActive`
- `oneDriveDestination`
- `freeAgentMatching`
- `createdAt`
- `updatedAt`

Notes:

- Do not store secrets in configuration records.
- Store references to secret names or configuration keys when needed.
- If multi-company support becomes necessary, reconsider the partition key and
  include a company or tenant identifier.

## invoice-records

Stores expected and retrieved invoice history.

Purpose:

- Track the next expected invoice.
- Track whether an expected invoice has been retrieved.
- Track OneDrive saved location.
- Track FreeAgent bill URL.
- Support retry and audit history.

Suggested partition key:

- `/configurationId`

Candidate fields:

- `id`
- `configurationId`
- `invoiceName`
- `integrationType`
- `expectedDate`
- `invoiceDate`
- `dateRetrieved`
- `status`
- `totalAmount`
- `currency`
- `vatMode`
- `sourceInvoiceId`
- `sourceMetadata`
- `oneDriveLocation`
- `freeAgentBillUrl`
- `lastError`
- `retryCount`
- `createdAt`
- `updatedAt`

Notes:

- `vatMode` should distinguish VAT inclusive (`inc`) and VAT exclusive (`exc`)
  totals.
- `sourceMetadata` may contain provider-specific non-secret metadata.
- The service should avoid creating duplicate records for the same expected
  invoice period.

## processing-runs

Stores summary information for each service execution.

Purpose:

- Review recent runs.
- Link logs and failures to a specific run.
- Record summary counts for monitoring and diagnosis.

Suggested partition key:

- `/runMonth`

Candidate fields:

- `id`
- `runMonth`
- `startedAt`
- `finishedAt`
- `status`
- `triggerType`
- `expectedInvoiceCount`
- `retrievedInvoiceCount`
- `savedToOneDriveCount`
- `uploadedToFreeAgentCount`
- `missingInvoiceCount`
- `failedInvoiceCount`
- `errorSummary`

## Query Patterns

The initial model should support these queries:

- Find all active invoice configurations.
- Find invoice records for a configuration.
- Find invoices expected on or before a date.
- Find invoices with failed or retryable status.
- Find recent processing runs.
- Find the latest record for a configured invoice.

## Consistency Expectations

The workflow should persist state after meaningful steps:

1. Expected invoice identified.
2. Invoice retrieved.
3. Invoice saved to OneDrive.
4. Invoice uploaded to FreeAgent.
5. Processing completed or failed.

This allows retry after partial failure without losing progress.

## Open Data Decisions

These decisions should be revisited during implementation:

- Exact partition keys after concrete query patterns are known.
- Whether expected and retrieved invoices remain in one container.
- Whether provider-specific metadata needs separate typed records.
- Whether manual override or reconciliation records are required.
