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

        var configurations = await configurationRepository.ListActiveAsync(cancellationToken);
        var configurationsById = configurations.ToDictionary(configuration => configuration.Id);

        var dueRecords = await recordRepository.ListDueAsync(asOf, cancellationToken);
        var results = new List<DueInvoiceProcessingResult>(dueRecords.Count);

        logger.LogInformation("Due invoice processing run started for {DueRecordCount} record(s) as of {AsOf}.", dueRecords.Count, asOf);

        foreach (var record in dueRecords)
        {
            // Skip records whose configuration is no longer active or present.
            if (!configurationsById.TryGetValue(record.ConfigurationId, out var configuration))
                continue;

            using var scope = logger.BeginScope(new Dictionary<string, object>
            {
                ["RecordId"] = record.Id.Value,
                ["ConfigurationId"] = record.ConfigurationId.Value,
                ["IntegrationType"] = configuration.IntegrationType.ToString(),
                ["InvoiceDescription"] = record.InvoiceDescription,
                ["ExpectedDate"] = record.ExpectedDate,
            });

            try
            {
                results.Add(await ProcessAsync(record, configuration, asOf, cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failure outside retrieval (for example a save or next-record step) leaves
                // the record in its last persisted, already-retryable state. Report it without
                // stopping the other records.
                logger.LogError(ex, "Processing failed for invoice record {RecordId}.", record.Id);
                results.Add(new ProcessingFailed(record.Id, ex));
            }
        }

        LogRunSummary(results);
        return results;
    }

    private async Task<DueInvoiceProcessingResult> ProcessAsync(
        InvoiceRecord record,
        InvoiceConfiguration configuration,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        if (!sourcesByType.TryGetValue(configuration.IntegrationType, out var source))
        {
            throw new InvalidOperationException(
                $"No invoice source integration is registered for integration type '{configuration.IntegrationType}'.");
        }

        var criteria = new InvoiceSearchCriteria(
            configuration.BillingAccountId,
            record.ExpectedDate,
            record.DateToleranceDays,
            record.ExpectedAmount,
            record.AmountTolerance);

        InvoiceSourceResult result;
        try
        {
            result = await source.FindInvoiceAsync(criteria, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A technical failure during retrieval: the system cannot tell whether the
            // invoice exists. Record RetrievalError (always retryable) and move on.
            var errored = record with { State = new RetrievalError(ex.Message) };
            await recordRepository.ReplaceAsync(errored, cancellationToken);
            logger.LogError(ex, "Retrieval failed for invoice record {RecordId}; marked RetrievalError.", record.Id);
            return new ProcessingFailed(record.Id, ex);
        }

        if (result is not InvoiceMatch match)
            return await RecordNoMatchAsync(record, asOf, cancellationToken);

        // Retrieved: persist before saving so a later run resumes from here.
        var retrieved = record with { State = new Retrieved(match.Details) };
        await recordRepository.ReplaceAsync(retrieved, cancellationToken);

        var fileName = invoiceFilename.Generate(
            match.Details.ActualInvoiceDate,
            configuration.InvoiceDescription,
            match.Details.SourceInvoiceId.Value,
            match.Details.ActualAmount,
            configuration.DefaultVatMode);

        var oneDriveDetails = await oneDriveIntegration.UploadAsync(
            new OneDriveUploadRequest(configuration.OneDriveDestination, fileName, match.PdfContent),
            cancellationToken);

        // Saved to OneDrive: persist before creating the next expected record.
        var saved = retrieved with { State = new SavedToOneDrive(match.Details, oneDriveDetails) };
        await recordRepository.ReplaceAsync(saved, cancellationToken);

        await expectedRecordGenerator.GenerateAsync(configuration, cancellationToken);

        logger.LogInformation("Saved invoice {FileName} for record {RecordId}.", fileName, record.Id);
        return new ProcessingSucceeded(record.Id);
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
        CancellationToken cancellationToken)
    {
        var deadline = record.ExpectedDate.AddDays(record.DateToleranceDays);

        if (asOf < deadline)
        {
            // Only write when the state actually changes (e.g. clearing a prior
            // RetrievalError); an already-Expected record needs no write.
            if (record.State is not Expected)
                await recordRepository.ReplaceAsync(record with { State = new Expected() }, cancellationToken);
            logger.LogInformation(
                "No invoice match found yet for record {RecordId}; still expected, within tolerance until {Deadline}.",
                record.Id,
                deadline);
            return new ProcessingNoMatch(record.Id);
        }

        var notFound = record with { State = new NotFound() };
        await recordRepository.ReplaceAsync(notFound, cancellationToken);
        logger.LogWarning(
            "No invoice match found for record {RecordId} by tolerance deadline {Deadline}; marked NotFound.",
            record.Id,
            deadline);
        return new ProcessingNotFound(record.Id);
    }

    private void LogRunSummary(IReadOnlyList<DueInvoiceProcessingResult> results)
    {
        logger.LogInformation(
            "Due invoice processing run complete: {ProcessedCount} processed, {SavedCount} saved, " +
            "{NoMatchCount} no match yet, {NotFoundCount} not found, {FailedCount} failed.",
            results.Count,
            results.Count(r => r is ProcessingSucceeded),
            results.Count(r => r is ProcessingNoMatch),
            results.Count(r => r is ProcessingNotFound),
            results.Count(r => r is ProcessingFailed));
    }
}
