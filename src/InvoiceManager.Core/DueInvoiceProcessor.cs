using InvoiceManager.Core.Integrations;
using InvoiceManager.Core.Repositories;
using Microsoft.Extensions.Logging;

namespace InvoiceManager.Core;

/// <summary>
/// Processes invoice records that are due for retrieval: for each due record it
/// asks the matching source integration for the invoice, saves it to OneDrive,
/// and creates the next expected record. State is persisted after each step so a
/// later run can continue without repeating completed work. A failure for one
/// record is isolated and reported without stopping the others.
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

        foreach (var record in dueRecords)
        {
            // Skip records whose configuration is no longer active or present.
            if (!configurationsById.TryGetValue(record.ConfigurationId, out var configuration))
                continue;

            try
            {
                results.Add(await ProcessAsync(record, configuration, cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Processing failed for invoice record {RecordId}.", record.Id);
                results.Add(new ProcessingFailed(record.Id, ex));
            }
        }

        return results;
    }

    private async Task<DueInvoiceProcessingResult> ProcessAsync(
        InvoiceRecord record,
        InvoiceConfiguration configuration,
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

        var result = await source.FindInvoiceAsync(criteria, cancellationToken);
        if (result is not InvoiceMatch match)
        {
            logger.LogInformation("No invoice match found yet for record {RecordId}.", record.Id);
            return new ProcessingSkippedNoMatch(record.Id);
        }

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
}
