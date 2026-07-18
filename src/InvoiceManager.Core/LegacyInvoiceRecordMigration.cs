using InvoiceManager.Core.Repositories;

namespace InvoiceManager.Core;

public sealed record LegacyRecordMigrationFailure(InvoiceConfigurationId ConfigurationId, string Message);

public sealed record LegacyRecordMigrationResult(
    int Migrated,
    int Skipped,
    int Failed,
    IReadOnlyList<LegacyRecordMigrationFailure> Failures);

/// <summary>
/// Provider-independent, idempotent migration that snapshots live routing values
/// onto retryable legacy records.
/// </summary>
public sealed class LegacyInvoiceRecordMigration(
    IInvoiceRecordRepository recordRepository,
    IInvoiceConfigurationRepository configurationRepository)
{
    public async Task<int> CountPendingAsync(CancellationToken cancellationToken = default) =>
        (await recordRepository.ListRetryableForMigrationAsync(cancellationToken))
            .Count(record => record.ProcessingSnapshot is null);

    public async Task<LegacyRecordMigrationResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var records = await recordRepository.ListRetryableForMigrationAsync(cancellationToken);
        var configurations = await configurationRepository.ListAllAsync(cancellationToken);
        var byId = configurations.ToDictionary(x => x.Configuration.Id, x => x.Configuration);
        var migrated = 0;
        var skipped = 0;
        var failures = new List<LegacyRecordMigrationFailure>();

        foreach (var record in records)
        {
            if (record.ProcessingSnapshot is not null)
            {
                skipped++;
                continue;
            }

            if (!byId.TryGetValue(record.ConfigurationId, out var configuration))
            {
                failures.Add(new(record.ConfigurationId, "The live invoice configuration was not found."));
                continue;
            }

            try
            {
                await recordRepository.ReplaceAsync(
                    record with { ProcessingSnapshot = InvoiceProcessingSnapshot.FromConfiguration(configuration) },
                    cancellationToken);
                migrated++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add(new(record.ConfigurationId, ex.Message));
            }
        }

        return new(migrated, skipped, failures.Count, failures);
    }
}
