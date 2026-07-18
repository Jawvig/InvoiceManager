using System.Diagnostics;
using InvoiceManager.Core.Integrations;
using InvoiceManager.Core.Repositories;
using Microsoft.Extensions.Logging;

namespace InvoiceManager.Core;

/// <summary>
/// Processes invoice records that are due for retrieval: for each due record it
/// asks the matching source integration for the invoice, saves it to OneDrive,
/// and creates the next expected record. Records that are not yet available stay
/// <see cref="Expected"/> (retried on later runs) until their tolerance window
/// elapses, after which they move to the terminal <see cref="NotFound"/>.
/// A technical failure moves the record to <see cref="RetrievalError"/> (always
/// retried) and is isolated so the other records still run. State is persisted
/// after each step so a later run can continue without repeating completed work.
/// Structured telemetry is emitted per record and as a run summary.
/// </summary>
public sealed class DueInvoiceProcessor(
    IInvoiceRecordRepository recordRepository,
    IInvoiceConfigurationRepository configurationRepository,
    IEnumerable<IInvoiceSourceIntegration> sourceIntegrations,
    IOneDriveIntegration oneDriveIntegration,
    InvoiceFilename invoiceFilename,
    ExpectedRecordGenerator expectedRecordGenerator,
    TimeProvider timeProvider,
    ILogger<DueInvoiceProcessor> logger)
{
    private readonly IReadOnlyDictionary<IntegrationType, IInvoiceSourceIntegration> sourcesByType =
        sourceIntegrations.ToDictionary(integration => integration.IntegrationType);

    /// <summary>
    /// Processes every due record (expected date on or before today, still
    /// awaiting retrieval or a retryable save). Returns a per-record outcome for
    /// each record processed.
    /// </summary>
    public async Task<IReadOnlyList<DueInvoiceProcessingResult>> ProcessDueAsync(
        CancellationToken cancellationToken = default)
    {
        var asOf = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        using var runActivity = Telemetry.ActivitySource.StartActivity("process_due_invoices");
        runActivity?.SetTag("invoice.as_of", asOf.ToString("O"));

        var configurations = await configurationRepository.ListActiveAsync(cancellationToken);
        var configurationsById = configurations.ToDictionary(configuration => configuration.Id);

        var dueRecords = await recordRepository.ListDueAsync(asOf, cancellationToken);
        var results = new List<DueInvoiceProcessingResult>(dueRecords.Count);

        runActivity?.SetTag("invoice.due_count", dueRecords.Count);
        logger.LogInformation("Due invoice processing run started for {DueRecordCount} record(s) as of {AsOf}.", dueRecords.Count, asOf);

        var skippedCount = 0;
        foreach (var record in dueRecords)
        {
            // Skip records whose configuration is no longer active or present: nothing
            // further can be done for them this run, so record why and move on.
            if (!configurationsById.TryGetValue(record.ConfigurationId, out var configuration))
            {
                skippedCount++;
                runActivity?.AddEvent(new ActivityEvent("record_skipped_inactive_configuration",
                    tags: new ActivityTagsCollection
                    {
                        ["invoice.record_id"] = record.Id.Value,
                        ["invoice.configuration_id"] = record.ConfigurationId.Value,
                    }));
                logger.LogInformation(
                    "Skipping due record {RecordId}: configuration {ConfigurationId} is no longer active or present; no action taken.",
                    record.Id, record.ConfigurationId);
                continue;
            }

            using var recordActivity = Telemetry.ActivitySource.StartActivity("process_invoice_record");
            recordActivity?.SetTag("invoice.record_id", record.Id.Value);
            recordActivity?.SetTag("invoice.configuration_id", record.ConfigurationId.Value);
            var snapshot = record.ProcessingSnapshot;
            recordActivity?.SetTag("invoice.integration_type", snapshot.IntegrationType.ToString());
            recordActivity?.SetTag("invoice.description", snapshot.InvoiceDescription);
            recordActivity?.SetTag("invoice.expected_date", record.ExpectedDate.ToString("O"));

            using var scope = logger.BeginScope(new Dictionary<string, object>
            {
                ["RecordId"] = record.Id.Value,
                ["ConfigurationId"] = record.ConfigurationId.Value,
                ["IntegrationType"] = snapshot.IntegrationType.ToString(),
                ["InvoiceDescription"] = snapshot.InvoiceDescription,
                ["ExpectedDate"] = record.ExpectedDate,
            });

            try
            {
                var result = await ProcessAsync(record, configuration, snapshot, asOf, recordActivity, cancellationToken);
                recordActivity?.SetTag("invoice.outcome", OutcomeName(result));
                results.Add(result);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failure outside retrieval (for example a save or next-record step) leaves
                // the record in its last persisted, already-retryable state. Report it without
                // stopping the other records.
                recordActivity?.SetTag("invoice.outcome", "failed");
                recordActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                recordActivity?.AddException(ex);
                logger.LogError(ex, "Processing failed for invoice record {RecordId}.", record.Id);
                results.Add(new ProcessingFailed(record.Id, ex));
            }
        }

        runActivity?.SetTag("invoice.skipped_count", skippedCount);
        SetRunSummaryTags(runActivity, results);
        LogRunSummary(results);
        return results;
    }

    private static string OutcomeName(DueInvoiceProcessingResult result) => result switch
    {
        ProcessingSucceeded => "saved",
        ProcessingReconciled => "reconciled",
        ProcessingNoMatch => "no_match",
        ProcessingNotFound => "not_found",
        ProcessingFailed => "failed",
        _ => "unknown",
    };

    private async Task<DueInvoiceProcessingResult> ProcessAsync(
        InvoiceRecord record,
        InvoiceConfiguration configuration,
        InvoiceProcessingSnapshot snapshot,
        DateOnly asOf,
        Activity? recordActivity,
        CancellationToken cancellationToken)
    {
        if (!sourcesByType.TryGetValue(snapshot.IntegrationType, out var source))
        {
            throw new InvalidOperationException(
                $"No invoice source integration is registered for integration type '{snapshot.IntegrationType}'.");
        }

        var criteria = new InvoiceSearchCriteria(
            snapshot.BillingAccountId,
            record.ExpectedDate,
            snapshot.DateToleranceDays,
            snapshot.AmountMatchingCriteria,
            snapshot.SenderEmailAddress,
            snapshot.BodyPattern);

        // Reconcile first: a file already in OneDrive (a manual download or an
        // earlier partial run) is used as-is, skipping the source call and upload.
        // The description is part of the match so records for different subscriptions
        // sharing one folder do not reconcile against each other's files.
        var oneDriveCriteria = new OneDriveSearchCriteria(
            record.ExpectedDate,
            snapshot.DateToleranceDays,
            snapshot.AmountMatchingCriteria,
            snapshot.InvoiceDescription);

        OneDriveSearchResult search;
        try
        {
            search = await oneDriveIntegration.SearchAsync(
                new OneDriveSearchRequest(snapshot.OneDriveDestination, oneDriveCriteria), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await MarkRetrievalErrorAsync(
                record, ex, recordActivity, "OneDrive reconciliation search", cancellationToken);
        }

        if (search is OneDriveMatch reconciledMatch)
            return await ReconcileAsync(record, configuration, reconciledMatch, recordActivity, cancellationToken);

        // No existing file: fall through to the source integration.
        InvoiceSourceResult result;
        try
        {
            result = await source.FindInvoiceAsync(criteria, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A technical failure during retrieval: the system cannot tell whether the
            // invoice exists. Record RetrievalError (always retryable) and move on.
            return await MarkRetrievalErrorAsync(record, ex, recordActivity, "Retrieval", cancellationToken);
        }

        if (result is not InvoiceMatch match)
            return await RecordNoMatchAsync(record, asOf, recordActivity, cancellationToken);

        // Retrieved: persist before saving so a later run resumes from here.
        var retrieved = record with { State = new Retrieved(match.Details) };
        await recordRepository.ReplaceAsync(retrieved, cancellationToken);
        recordActivity?.AddEvent(new ActivityEvent("state_retrieved"));
        logger.LogInformation(
            "Invoice {SourceInvoiceId} retrieved for record {RecordId}; marked Retrieved before saving.",
            match.Details.SourceInvoiceId.Value, record.Id);

        var fileName = invoiceFilename.Generate(
            match.Details.ActualInvoiceDate,
            snapshot.InvoiceDescription,
            match.Details.SourceInvoiceId.Value,
            match.Details.ActualAmount,
            snapshot.VatMode);

        var oneDriveDetails = await oneDriveIntegration.UploadAsync(
            new OneDriveUploadRequest(snapshot.OneDriveDestination, fileName, match.PdfContent),
            cancellationToken);

        // Saved to OneDrive: persist before creating the next expected record.
        var saved = retrieved with { State = new SavedToOneDrive(match.Details, oneDriveDetails) };
        await recordRepository.ReplaceAsync(saved, cancellationToken);
        recordActivity?.AddEvent(new ActivityEvent("state_saved_to_onedrive"));

        await expectedRecordGenerator.GenerateAsync(configuration, cancellationToken);

        logger.LogInformation("Saved invoice {FileName} for record {RecordId}.", fileName, record.Id);
        return new ProcessingSucceeded(record.Id);
    }

    /// <summary>
    /// Records a match against a file already in OneDrive: sets the record to
    /// <see cref="ReconciledFromOneDrive"/> (with the match reason and time) and
    /// creates the next expected record, without calling the source or uploading.
    /// </summary>
    private async Task<DueInvoiceProcessingResult> ReconcileAsync(
        InvoiceRecord record,
        InvoiceConfiguration configuration,
        OneDriveMatch match,
        Activity? recordActivity,
        CancellationToken cancellationToken)
    {
        var reconciled = record with
        {
            State = new ReconciledFromOneDrive(
                match.Details,
                match.OneDriveDetails,
                match.MatchReason,
                timeProvider.GetUtcNow()),
        };
        await recordRepository.ReplaceAsync(reconciled, cancellationToken);
        recordActivity?.AddEvent(new ActivityEvent("state_reconciled_from_onedrive"));
        logger.LogInformation(
            "Reconciled record {RecordId} against existing OneDrive file at {Location}; skipping source retrieval.",
            record.Id, match.OneDriveDetails.OneDriveLocation);

        await expectedRecordGenerator.GenerateAsync(configuration, cancellationToken);
        return new ProcessingReconciled(record.Id);
    }

    /// <summary>
    /// Marks a record <see cref="RetrievalError"/> (always retryable) after a
    /// technical failure — a reconciliation search or source call that could not
    /// determine whether the invoice exists — and reports it without stopping the
    /// other records.
    /// </summary>
    private async Task<DueInvoiceProcessingResult> MarkRetrievalErrorAsync(
        InvoiceRecord record,
        Exception ex,
        Activity? recordActivity,
        string stage,
        CancellationToken cancellationToken)
    {
        var errored = record with { State = new RetrievalError(ex.Message) };
        await recordRepository.ReplaceAsync(errored, cancellationToken);
        recordActivity?.AddEvent(new ActivityEvent("state_retrieval_error"));
        recordActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        recordActivity?.AddException(ex);
        logger.LogError(ex, "{Stage} failed for invoice record {RecordId}; marked RetrievalError.", stage, record.Id);
        return new ProcessingFailed(record.Id, ex);
    }

    /// <summary>
    /// Records the absence of a source match. Within the tolerance window the
    /// record stays <see cref="Expected"/> so a later run retries it (a prior
    /// <see cref="RetrievalError"/> is cleared back to <see cref="Expected"/> once a
    /// clean poll returns no match); on or after the deadline it is set to the
    /// terminal <see cref="NotFound"/>. Because the deadline is checked against
    /// every run, a record processed for the first time after its window has
    /// elapsed goes straight to <see cref="NotFound"/>.
    /// </summary>
    private async Task<DueInvoiceProcessingResult> RecordNoMatchAsync(
        InvoiceRecord record,
        DateOnly asOf,
        Activity? recordActivity,
        CancellationToken cancellationToken)
    {
        var deadline = record.ExpectedDate.AddDays(record.ProcessingSnapshot.DateToleranceDays);

        if (asOf < deadline)
        {
            // Only write when the state actually changes (e.g. clearing a prior
            // RetrievalError); an already-Expected record needs no write.
            if (record.State is not Expected)
                await recordRepository.ReplaceAsync(record with { State = new Expected() }, cancellationToken);
            recordActivity?.AddEvent(new ActivityEvent("no_match_within_tolerance"));
            logger.LogInformation(
                "No invoice match found yet for record {RecordId}; still expected, within tolerance until {Deadline}.",
                record.Id,
                deadline);
            return new ProcessingNoMatch(record.Id);
        }

        var notFound = record with { State = new NotFound() };
        await recordRepository.ReplaceAsync(notFound, cancellationToken);
        recordActivity?.AddEvent(new ActivityEvent("state_not_found_past_deadline"));
        logger.LogWarning(
            "No invoice match found for record {RecordId} by tolerance deadline {Deadline}; marked NotFound.",
            record.Id,
            deadline);
        return new ProcessingNotFound(record.Id);
    }

    private static void SetRunSummaryTags(Activity? activity, IReadOnlyList<DueInvoiceProcessingResult> results)
    {
        if (activity is null)
            return;

        activity.SetTag("invoice.processed_count", results.Count);
        activity.SetTag("invoice.saved_count", results.Count(r => r is ProcessingSucceeded));
        activity.SetTag("invoice.reconciled_count", results.Count(r => r is ProcessingReconciled));
        activity.SetTag("invoice.no_match_count", results.Count(r => r is ProcessingNoMatch));
        activity.SetTag("invoice.not_found_count", results.Count(r => r is ProcessingNotFound));
        activity.SetTag("invoice.failed_count", results.Count(r => r is ProcessingFailed));
    }

    private void LogRunSummary(IReadOnlyList<DueInvoiceProcessingResult> results)
    {
        logger.LogInformation(
            "Due invoice processing run complete: {ProcessedCount} processed, {SavedCount} saved, " +
            "{ReconciledCount} reconciled, {NoMatchCount} no match yet, {NotFoundCount} not found, {FailedCount} failed.",
            results.Count,
            results.Count(r => r is ProcessingSucceeded),
            results.Count(r => r is ProcessingReconciled),
            results.Count(r => r is ProcessingNoMatch),
            results.Count(r => r is ProcessingNotFound),
            results.Count(r => r is ProcessingFailed));
    }
}
