using System.Globalization;
using System.Net;
using InvoiceManager.Core;
using InvoiceManager.Core.Repositories;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace InvoiceManager.Infrastructure.CosmosDb;

/// <summary>
/// Cosmos DB implementation of <see cref="IInvoiceRecordRepository"/>.
/// Container: <c>invoice-records</c>, partition key: <c>/configurationId</c>.
/// </summary>
public sealed class CosmosInvoiceRecordRepository : IInvoiceRecordRepository
{
    private const string ContainerName = "invoice-records";

    private readonly Container container;
    private readonly ILogger<CosmosInvoiceRecordRepository> logger;

    public CosmosInvoiceRecordRepository(
        CosmosClient cosmosClient,
        string databaseName,
        ILogger<CosmosInvoiceRecordRepository>? logger = null)
    {
        container = cosmosClient.GetContainer(databaseName, ContainerName);
        this.logger = logger ?? NullLogger<CosmosInvoiceRecordRepository>.Instance;
    }

    public async Task<Option<InvoiceRecord>> GetMostRecentAsync(
        InvoiceConfigurationId configurationId,
        CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition(
            "SELECT TOP 1 * FROM c WHERE c.configurationId = @configurationId ORDER BY c.expectedDate DESC")
            .WithParameter("@configurationId", configurationId.Value);

        using var iterator = container.GetItemQueryIterator<InvoiceRecordDocument>(
            query,
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(configurationId.Value),
            });

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            foreach (var document in page)
                return document.ToRecord();
        }

        return Option.None;
    }

    public async Task CreateIfNotExistsAsync(InvoiceRecord record, CancellationToken cancellationToken = default)
    {
        var document = InvoiceRecordDocument.FromRecord(record);
        var partitionKey = new PartitionKey(record.ConfigurationId.Value);

        try
        {
            await container.CreateItemAsync(document, partitionKey, cancellationToken: cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // Record already exists for this configuration and expected date — idempotent no-op.
            // Logged so an operator can see that no new record was created (and why).
            logger.LogDebug(
                "Expected record for configuration {ConfigurationId} on {ExpectedDate} already exists; create skipped (idempotent no-op).",
                record.ConfigurationId, record.ExpectedDate);
        }
    }

    public async Task<IReadOnlyList<InvoiceRecord>> ListDueAsync(
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        // Cross-partition: due records may belong to any configuration. Retryable
        // statuses (Expected, RetrievalError, Retrieved) are all picked up; the
        // terminal NotFound state is excluded.
        var query = new QueryDefinition(
            "SELECT * FROM c " +
            "WHERE c.status IN (@expectedStatus, @retrievalErrorStatus, @retrievedStatus) " +
            "AND c.expectedDate <= @asOf")
            .WithParameter("@expectedStatus", nameof(Expected))
            .WithParameter("@retrievalErrorStatus", nameof(RetrievalError))
            .WithParameter("@retrievedStatus", nameof(Retrieved))
            .WithParameter("@asOf", asOf.ToString("O", CultureInfo.InvariantCulture));

        using var iterator = container.GetItemQueryIterator<InvoiceRecordDocument>(query);

        var records = new List<InvoiceRecord>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            foreach (var document in page)
                records.Add(document.ToRecord());
        }

        return records;
    }

    public async Task ReplaceAsync(InvoiceRecord record, CancellationToken cancellationToken = default)
    {
        var document = InvoiceRecordDocument.FromRecord(record);
        var partitionKey = new PartitionKey(record.ConfigurationId.Value);

        await container.UpsertItemAsync(document, partitionKey, cancellationToken: cancellationToken);
    }
}
