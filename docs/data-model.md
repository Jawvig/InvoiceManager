# Data Model

InvoiceManager uses Azure Cosmos DB for NoSQL in serverless mode. The model
should be optimized for operational queries rather than normalized like a
relational database.

## Containers

Initial containers:

- `invoice-configurations`
- `invoice-records`
- `processing-runs`

This can change during implementation if concrete query patterns show a better
shape.

## invoice-configurations

Stores recurring invoice expectations and provider configuration references.

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
- Store secret names or configuration-key references where needed.
- Separate Microsoft 365 invoices, such as Copilot and Office 365 extensions,
  should usually be separate configuration records even when they share an
  integration type.
- If multi-company support becomes necessary, reconsider the partition key and
  include a company or tenant identifier.

## invoice-records

Stores expected invoice history, retrieval state, reconciliation state, and
downstream attachment state.

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

- Expected fields are criteria and should not be overwritten by actual values.
- `expectedVatMode` and `actualVatMode` distinguish VAT inclusive (`inc`) and
  VAT exclusive (`exc`) totals.
- Amount comparisons must include currency.
- `sourceMetadata` may contain provider-specific non-secret metadata.
- `matchReason` should preserve why a candidate was accepted.
- `reconciliationSource` can distinguish automatic OneDrive scans from future
  manual override or migration tooling.
- The service should avoid duplicate records for the same configuration, period,
  amount, and currency.

## processing-runs

Stores summary information for each service execution.

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

The initial model should support:

- Finding active invoice configurations.
- Finding invoice records for a configuration.
- Finding invoices expected on or before a date.
- Finding records that need OneDrive reconciliation or retry.
- Finding recent processing runs.
- Finding the latest completed record for a configured invoice.
- Detecting whether the next expected record already exists.
- Finding possible duplicate records by configuration, expected date, amount,
  and currency.

## Consistency Expectations

The workflow should persist after these meaningful steps:

1. Expected invoice identified.
2. Existing OneDrive file checked.
3. Existing OneDrive file reconciled, if present.
4. Invoice retrieved from source, if not already present in OneDrive.
5. Invoice saved to OneDrive, if newly retrieved.
6. Invoice uploaded to FreeAgent.
7. Next expected invoice record created.
8. Processing completed or failed.

Creating the next expected invoice record must be idempotent. A retry or manual
rerun should detect an existing next record for the same configuration and
period rather than creating a duplicate.

## Open Decisions

- Exact partition keys after concrete query patterns are known.
- Whether expected and retrieved invoice state remains in one container.
- Whether provider-specific metadata needs separate typed records.
- Whether manual override events need their own records.
