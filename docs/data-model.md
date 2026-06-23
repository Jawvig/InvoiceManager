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
- Determine default expected amount and currency where useful for matching.
- Determine OneDrive and FreeAgent behavior.

Suggested partition key:

- `/integrationType`

Candidate fields:

- `id`
- `integrationType`
- `invoiceName`
- `expectedFrequency`
- `defaultExpectedAmount`
- `defaultExpectedCurrency`
- `defaultVatMode`
- `isActive`
- `oneDriveDestination`
- `freeAgentMatching`
- `createdAt`
- `updatedAt`

Notes:

- Do not store secrets in configuration records.
- Store references to secret names or configuration keys when needed.
- Separate Microsoft 365 invoices, such as Copilot and Office 365 extensions,
  should usually be separate configuration records even when they use the same
  `integrationType`.
- If multi-company support becomes necessary, reconsider the partition key and
  include a company or tenant identifier.

## invoice-records

Stores expected invoice processing history, including retrieval and
reconciliation outcomes.

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
- `expectedDateToleranceDays`
- `expectedAmount`
- `expectedCurrency`
- `expectedVatMode`
- `actualInvoiceDate`
- `actualAmount`
- `actualCurrency`
- `actualVatMode`
- `dateRetrieved`
- `status`
- `sourceInvoiceId`
- `sourceMetadata`
- `matchStatus`
- `matchReason`
- `reconciledFromOneDrive`
- `reconciledAt`
- `reconciliationSource`
- `oneDriveLocation`
- `oneDriveFileId`
- `freeAgentBillUrl`
- `nextInvoiceRecordId`
- `nextInvoiceCreatedAt`
- `lastError`
- `retryCount`
- `createdAt`
- `updatedAt`

Notes:

- Expected fields are the criteria used to find the invoice. Actual fields are
  populated after retrieval or OneDrive reconciliation and should not overwrite
  the expected values.
- `expectedVatMode` and `actualVatMode` should distinguish VAT inclusive (`inc`)
  and VAT exclusive (`exc`) totals.
- Amount comparisons must include currency. OpenAI invoices may be in USD while
  most other invoices are expected to be in GBP.
- `sourceMetadata` may contain provider-specific non-secret metadata.
- `matchReason` should preserve why a candidate was accepted, such as matching
  expected date and amount within the configured tolerance.
- `reconciliationSource` can distinguish automatic OneDrive scans from future
  manual override or migration tooling without requiring an admin UI now.
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
- `reconciledFromOneDriveCount`
- `savedToOneDriveCount`
- `uploadedToFreeAgentCount`
- `nextInvoiceCreatedCount`
- `missingInvoiceCount`
- `failedInvoiceCount`
- `errorSummary`

## Query Patterns

The initial model should support these queries:

- Find all active invoice configurations.
- Find invoice records for a configuration.
- Find invoices expected on or before a date.
- Find invoice records that need OneDrive reconciliation.
- Find invoices with failed or retryable status.
- Find recent processing runs.
- Find the latest record for a configured invoice.
- Find whether the next expected record already exists for a configuration and
  period.
- Find possible duplicate records by configuration, expected date, amount, and
  currency.

## Consistency Expectations

The workflow should persist enough state after meaningful steps to allow retry
after partial failure without losing progress. The data model supports those
steps through status, retry, reconciliation, OneDrive, FreeAgent, and
next-invoice fields on `invoice-records`.

See
[workflow-reconciliation.md#status-transitions](workflow-reconciliation.md#status-transitions)
for the processing sequence and retry behavior.

## Open Data Decisions

These decisions should be revisited during implementation:

- Exact partition keys after concrete query patterns are known.
- Whether expected invoice records and processing outcomes remain in one
  container.
- Whether provider-specific metadata needs separate typed records.
- Whether manual override events need their own records or can be represented by
  reconciliation fields on invoice records.
