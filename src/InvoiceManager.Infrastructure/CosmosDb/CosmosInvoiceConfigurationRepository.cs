using System.Net;
using InvoiceManager.Core;
using InvoiceManager.Core.Repositories;
using Microsoft.Azure.Cosmos;

namespace InvoiceManager.Infrastructure.CosmosDb;

/// <summary>
/// Cosmos DB implementation of <see cref="IInvoiceConfigurationRepository"/>.
/// Container: <c>invoice-configurations</c>, partition key: <c>/integrationType</c>.
/// </summary>
public sealed class CosmosInvoiceConfigurationRepository : IInvoiceConfigurationRepository
{
    private readonly Container container;

    public CosmosInvoiceConfigurationRepository(CosmosClient cosmosClient, string databaseName)
    {
        container = cosmosClient.GetContainer(databaseName, CosmosSchema.InvoiceConfigurations.Name);
    }

    /// <summary>
    /// Returns <see cref="CosmosClientOptions"/> pre-configured with the STJ serializer
    /// used by this repository. All callers that create a <see cref="CosmosClient"/> for
    /// this repository must use these options to ensure consistent serialization.
    /// </summary>
    public static CosmosClientOptions BuildClientOptions() => new()
    {
        Serializer = new CosmosStjSerializer(),
    };

    public async Task<IReadOnlyList<InvoiceConfiguration>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.isActive = true");
        using var iterator = container.GetItemQueryIterator<InvoiceConfigurationDocument>(query);

        var results = new List<InvoiceConfiguration>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            foreach (var document in page)
            {
                results.Add(document.ToConfiguration());
            }
        }

        return results;
    }

    public async Task CreateIfNotExistsAsync(
        InvoiceConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var document = InvoiceConfigurationDocument.FromConfiguration(configuration);
        var partitionKey = new PartitionKey(document.IntegrationType);

        try
        {
            await container.CreateItemAsync(document, partitionKey, cancellationToken: cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // Configuration already exists — skip without overwriting to preserve manual edits.
        }
    }
}
