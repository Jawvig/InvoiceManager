using System.Net;
using InvoiceManager.Core;
using InvoiceManager.Core.Repositories;
using Microsoft.Azure.Cosmos;

namespace InvoiceManager.Infrastructure.CosmosDb;

/// <summary>
/// Cosmos configuration repository. Live configuration mutations and immutable
/// revision appends are committed atomically in the integration-type partition.
/// </summary>
public sealed class CosmosInvoiceConfigurationRepository : IInvoiceConfigurationRepository
{
    private readonly Container container;
    private readonly TimeProvider timeProvider;

    public CosmosInvoiceConfigurationRepository(
        CosmosClient cosmosClient,
        string databaseName,
        TimeProvider? timeProvider = null)
    {
        container = cosmosClient.GetContainer(databaseName, CosmosSchema.InvoiceConfigurations.Name);
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public static CosmosClientOptions BuildClientOptions() => new()
    {
        Serializer = new CosmosStjSerializer(),
    };

    public async Task<IReadOnlyList<InvoiceConfiguration>> ListActiveAsync(
        CancellationToken cancellationToken = default) =>
        (await QueryLiveAsync(
            "SELECT * FROM c WHERE c.isActive = true AND (NOT IS_DEFINED(c.documentType) OR c.documentType = @live)",
            cancellationToken)).Select(x => x.Configuration).ToList();

    public Task<IReadOnlyList<StoredInvoiceConfiguration>> ListAllAsync(
        CancellationToken cancellationToken = default) =>
        QueryLiveAsync(
            "SELECT * FROM c WHERE NOT IS_DEFINED(c.documentType) OR c.documentType = @live",
            cancellationToken);

    public async Task<Option<StoredInvoiceConfiguration>> GetAsync(
        InvoiceConfigurationId id,
        IntegrationType integrationType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await container.ReadItemAsync<InvoiceConfigurationDocument>(
                id.Value, new PartitionKey(integrationType.ToString()), cancellationToken: cancellationToken);
            if (response.Resource.DocumentType != InvoiceConfigurationDocument.LiveDocumentType)
                return Option.None;
            return new StoredInvoiceConfiguration(response.Resource.ToConfiguration(), response.ETag);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return Option.None;
        }
    }

    public async Task CreateIfNotExistsAsync(
        InvoiceConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var document = InvoiceConfigurationDocument.FromConfiguration(configuration);
        try
        {
            await container.CreateItemAsync(
                document,
                new PartitionKey(document.IntegrationType),
                cancellationToken: cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // Bootstrap seeding is insert-only and never overwrites UI-managed values.
        }
    }

    public async Task<StoredInvoiceConfiguration> CreateAsync(
        InvoiceConfiguration configuration,
        InvoiceConfigurationActor actor,
        CancellationToken cancellationToken = default)
    {
        if (await IdExistsAsync(configuration.Id, cancellationToken))
            throw new DuplicateInvoiceConfigurationException(
                $"Invoice configuration ID '{configuration.Id}' already exists.");

        var now = timeProvider.GetUtcNow();
        var revision = NewRevision(configuration, InvoiceConfigurationRevisionAction.Created, actor, now);
        var document = InvoiceConfigurationDocument.FromConfiguration(configuration);
        var batch = container.CreateTransactionalBatch(new PartitionKey(document.IntegrationType))
            .CreateItem(document)
            .CreateItem(InvoiceConfigurationRevisionDocument.FromRevision(revision));

        using var response = await batch.ExecuteAsync(cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new DuplicateInvoiceConfigurationException(
                $"Invoice configuration ID '{configuration.Id}' already exists.");
        EnsureBatchSucceeded(response);

        return await ReadRequiredAsync(configuration.Id, configuration.IntegrationType, cancellationToken);
    }

    public async Task<StoredInvoiceConfiguration> ReplaceAsync(
        InvoiceConfiguration configuration,
        string etag,
        InvoiceConfigurationRevisionAction action,
        InvoiceConfigurationActor actor,
        CancellationToken cancellationToken = default)
    {
        var current = await ReadRequiredAsync(configuration.Id, configuration.IntegrationType, cancellationToken);
        var revisions = await ListRevisionsAsync(configuration.Id, configuration.IntegrationType, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var document = InvoiceConfigurationDocument.FromConfiguration(configuration);
        var batch = container.CreateTransactionalBatch(new PartitionKey(document.IntegrationType));

        if (revisions.Count == 0)
        {
            var baseline = NewRevision(
                current.Configuration,
                InvoiceConfigurationRevisionAction.PreAuditBaseline,
                actor: null,
                now.AddTicks(-1));
            batch.CreateItem(InvoiceConfigurationRevisionDocument.FromRevision(baseline));
        }

        batch.ReplaceItem(
            document.Id,
            document,
            new TransactionalBatchItemRequestOptions { IfMatchEtag = etag });
        batch.CreateItem(InvoiceConfigurationRevisionDocument.FromRevision(
            NewRevision(configuration, action, actor, now)));

        using var response = await batch.ExecuteAsync(cancellationToken);
        if (response.StatusCode == HttpStatusCode.PreconditionFailed)
            throw new InvoiceConfigurationConflictException(
                "This configuration changed after the page was loaded. Reload and review the latest values before saving again.");
        EnsureBatchSucceeded(response);

        return await ReadRequiredAsync(configuration.Id, configuration.IntegrationType, cancellationToken);
    }

    public async Task<IReadOnlyList<InvoiceConfigurationRevision>> ListRevisionsAsync(
        InvoiceConfigurationId id,
        IntegrationType integrationType,
        CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.documentType = @revision AND c.configurationId = @id ORDER BY c.timestamp ASC")
            .WithParameter("@revision", InvoiceConfigurationRevisionDocument.RevisionDocumentType)
            .WithParameter("@id", id.Value);
        using var iterator = container.GetItemQueryIterator<InvoiceConfigurationRevisionDocument>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(integrationType.ToString()) });
        var results = new List<InvoiceConfigurationRevision>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(page.Select(x => x.ToRevision()));
        }
        return results;
    }

    private async Task<IReadOnlyList<StoredInvoiceConfiguration>> QueryLiveAsync(
        string queryText,
        CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(queryText)
            .WithParameter("@live", InvoiceConfigurationDocument.LiveDocumentType);
        using var iterator = container.GetItemQueryIterator<InvoiceConfigurationDocument>(query);
        var results = new List<StoredInvoiceConfiguration>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(page.Select(document =>
                new StoredInvoiceConfiguration(document.ToConfiguration(), document.ETag)));
        }
        return results;
    }

    private async Task<bool> IdExistsAsync(InvoiceConfigurationId id, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                "SELECT VALUE COUNT(1) FROM c WHERE c.id = @id AND (NOT IS_DEFINED(c.documentType) OR c.documentType = @live)")
            .WithParameter("@id", id.Value)
            .WithParameter("@live", InvoiceConfigurationDocument.LiveDocumentType);
        using var iterator = container.GetItemQueryIterator<int>(query);
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            if (page.Resource.Sum() > 0)
                return true;
        }
        return false;
    }

    private async Task<StoredInvoiceConfiguration> ReadRequiredAsync(
        InvoiceConfigurationId id,
        IntegrationType integrationType,
        CancellationToken cancellationToken) =>
        await GetAsync(id, integrationType, cancellationToken) switch
        {
            StoredInvoiceConfiguration stored => stored,
            None => throw new KeyNotFoundException($"Invoice configuration '{id}' was not found."),
        };

    private static InvoiceConfigurationRevision NewRevision(
        InvoiceConfiguration configuration,
        InvoiceConfigurationRevisionAction action,
        InvoiceConfigurationActor? actor,
        DateTimeOffset timestamp) =>
        new(
            $"revision-{configuration.Id.Value}-{Guid.NewGuid():N}",
            configuration.Id,
            configuration.IntegrationType,
            action,
            timestamp,
            actor?.ObjectId,
            actor?.DisplayName ?? "Imported pre-audit baseline",
            configuration);

    private static void EnsureBatchSucceeded(TransactionalBatchResponse response)
    {
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Cosmos configuration transaction failed with {(int)response.StatusCode} {response.StatusCode}. {response.ErrorMessage}");
    }
}
