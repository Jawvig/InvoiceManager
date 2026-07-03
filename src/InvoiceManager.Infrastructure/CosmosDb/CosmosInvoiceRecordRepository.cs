using System.Net;
using InvoiceManager.Core;
using InvoiceManager.Core.Repositories;
using Microsoft.Azure.Cosmos;

namespace InvoiceManager.Infrastructure.CosmosDb;

/// <summary>
/// Cosmos DB implementation of <see cref="IInvoiceRecordRepository"/>.
/// Container: <c>invoice-records</c>, partition key: <c>/configurationId</c>.
/// </summary>
public sealed class CosmosInvoiceRecordRepository : IInvoiceRecordRepository
{
    private const string ContainerName = "invoice-records";

    private readonly Container container;

    public CosmosInvoiceRecordRepository(CosmosClient cosmosClient, string databaseName)
    {
        container = cosmosClient.GetContainer(databaseName, ContainerName);
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
        }
    }
}
