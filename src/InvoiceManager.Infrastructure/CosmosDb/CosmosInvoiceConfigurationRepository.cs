using System.Net;
using InvoiceManager.Core;
using InvoiceManager.Core.Repositories;
using Microsoft.Azure.Cosmos;

namespace InvoiceManager.Infrastructure.CosmosDb;

/// <summary>
/// Cosmos configuration repository. Live configuration mutations and immutable
/// revision appends are committed atomically in one configuration partition.
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
            "SELECT * FROM c WHERE c.isActive = true AND c.documentType = @live",
            cancellationToken)).Select(x => x.Configuration).ToList();

    public Task<IReadOnlyList<StoredInvoiceConfiguration>> ListAllAsync(
        CancellationToken cancellationToken = default) =>
        QueryLiveAsync(
            "SELECT * FROM c WHERE c.documentType = @live",
            cancellationToken);

    public async Task<Option<StoredInvoiceConfiguration>> GetAsync(
        InvoiceConfigurationId id,
        IntegrationType integrationType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await container.ReadItemAsync<InvoiceConfigurationDocument>(
                id.Value, ConfigurationPartition, cancellationToken: cancellationToken);
            if (response.Resource.DocumentType != InvoiceConfigurationDocument.LiveDocumentType ||
                !string.Equals(response.Resource.IntegrationType, integrationType.ToString(), StringComparison.OrdinalIgnoreCase))
                return Option.None;
            return new StoredInvoiceConfiguration(response.Resource.ToConfiguration(), response.ETag);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return Option.None;
        }
    }

    /// <summary>
    /// Bootstrap seeding is insert-only and never overwrites UI-managed values - if the
    /// configuration already exists, this is a no-op. Otherwise, the insert also advances the
    /// duplicate-validation sentinel in the same transactional batch (see
    /// <see cref="ConfigurationValidationSentinel"/> and docs/data-model.md): deploys run
    /// Terraform apply (which can start routing traffic to a live AdminWeb instance) before
    /// invoking the seeder, so a seeded configuration can genuinely race a concurrent
    /// Create/Update/Restore request through AdminWeb - see <c>scripts/Deploy-Infra.ps1</c>'s
    /// <c>Invoke-ConfigurationSeeder</c> call sites, both of which run after
    /// <c>terraform apply</c>/an unchanged-plan early return, not before AdminWeb comes up.
    /// Without this, an AdminWeb request that read the sentinel and the configuration list before
    /// the seeder's insert could still commit search criteria that now conflicts with what the
    /// seeder just added - exactly the race the sentinel exists to close, just with the seeder as
    /// the other writer instead of another AdminWeb request.
    /// </summary>
    public async Task CreateIfNotExistsAsync(
        InvoiceConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var document = InvoiceConfigurationDocument.FromConfiguration(configuration);
        const int documentIndex = 0;
        const int sentinelIndex = 1;
        const int maxAttempts = 5;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var sentinel = await GetValidationSentinelAsync(cancellationToken);
            var batch = container.CreateTransactionalBatch(ConfigurationPartition)
                .CreateItem(document)
                .ReplaceItem(
                    ValidationSentinelDocument.SentinelId,
                    new ValidationSentinelDocument(),
                    new TransactionalBatchItemRequestOptions { IfMatchEtag = sentinel.ETag });

            using var response = await batch.ExecuteAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
                return;

            if (response[documentIndex].StatusCode == HttpStatusCode.Conflict)
                return; // Already seeded - insert-only, nothing to do, sentinel untouched.

            if (response[sentinelIndex].StatusCode == HttpStatusCode.PreconditionFailed && attempt < maxAttempts)
                continue; // Another writer advanced the sentinel first - re-read and retry the insert.

            throw BatchFailed(response);
        }
    }

    public async Task<ConfigurationValidationSentinel> GetValidationSentinelAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await container.ReadItemAsync<ValidationSentinelDocument>(
                ValidationSentinelDocument.SentinelId, ConfigurationPartition, cancellationToken: cancellationToken);
            return new ConfigurationValidationSentinel(response.ETag);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return await CreateValidationSentinelAsync(cancellationToken);
        }
    }

    public async Task<InvoiceConfigurationWriteResult> CreateAsync(
        InvoiceConfiguration configuration,
        InvoiceConfigurationActor actor,
        ConfigurationValidationSentinel sentinel,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var revision = NewRevision(configuration, InvoiceConfigurationRevisionAction.Created, actor, now);
        var document = InvoiceConfigurationDocument.FromConfiguration(configuration);
        const int documentIndex = 0;
        const int sentinelIndex = 2;
        var batch = container.CreateTransactionalBatch(ConfigurationPartition)
            .CreateItem(document)
            .CreateItem(InvoiceConfigurationRevisionDocument.FromRevision(revision))
            .ReplaceItem(
                ValidationSentinelDocument.SentinelId,
                new ValidationSentinelDocument(),
                new TransactionalBatchItemRequestOptions { IfMatchEtag = sentinel.ETag });

        using var response = await batch.ExecuteAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (response[documentIndex].StatusCode == HttpStatusCode.Conflict)
                return new DuplicateInvoiceConfigurationId(configuration.Id);
            if (response[sentinelIndex].StatusCode == HttpStatusCode.PreconditionFailed)
                return new ValidationSentinelConflict();
            throw BatchFailed(response);
        }

        return await ReadRequiredAsync(configuration.Id, configuration.IntegrationType, cancellationToken);
    }

    public async Task<InvoiceConfigurationWriteResult> ReplaceAsync(
        InvoiceConfiguration configuration,
        string etag,
        InvoiceConfigurationRevisionAction action,
        InvoiceConfigurationActor actor,
        Option<ConfigurationValidationSentinel> sentinel,
        CancellationToken cancellationToken = default)
    {
        var current = await ReadRequiredAsync(configuration.Id, configuration.IntegrationType, cancellationToken);
        var revisions = await ListRevisionsAsync(configuration.Id, configuration.IntegrationType, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var document = InvoiceConfigurationDocument.FromConfiguration(configuration);
        var batch = container.CreateTransactionalBatch(ConfigurationPartition);

        if (revisions.Count == 0)
        {
            var baseline = NewRevision(
                current.Configuration,
                InvoiceConfigurationRevisionAction.PreAuditBaseline,
                actor: null,
                now.AddTicks(-1));
            batch.CreateItem(InvoiceConfigurationRevisionDocument.FromRevision(baseline));
        }

        var documentIndex = revisions.Count == 0 ? 1 : 0;
        batch.ReplaceItem(
            document.Id,
            document,
            new TransactionalBatchItemRequestOptions { IfMatchEtag = etag });
        batch.CreateItem(InvoiceConfigurationRevisionDocument.FromRevision(
            NewRevision(configuration, action, actor, now)));

        var sentinelIndex = -1;
        if (sentinel is ConfigurationValidationSentinel sentinelValue)
        {
            sentinelIndex = documentIndex + 2;
            batch.ReplaceItem(
                ValidationSentinelDocument.SentinelId,
                new ValidationSentinelDocument(),
                new TransactionalBatchItemRequestOptions { IfMatchEtag = sentinelValue.ETag });
        }

        using var response = await batch.ExecuteAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (response[documentIndex].StatusCode == HttpStatusCode.PreconditionFailed)
                return new InvoiceConfigurationConflict();
            if (sentinelIndex >= 0 && response[sentinelIndex].StatusCode == HttpStatusCode.PreconditionFailed)
                return new ValidationSentinelConflict();
            throw BatchFailed(response);
        }

        return await ReadRequiredAsync(configuration.Id, configuration.IntegrationType, cancellationToken);
    }

    private async Task<ConfigurationValidationSentinel> CreateValidationSentinelAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await container.CreateItemAsync(
                new ValidationSentinelDocument(), ConfigurationPartition, cancellationToken: cancellationToken);
            return new ConfigurationValidationSentinel(created.ETag);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // Another concurrent bootstrap call created it first - read what it wrote.
            var response = await container.ReadItemAsync<ValidationSentinelDocument>(
                ValidationSentinelDocument.SentinelId, ConfigurationPartition, cancellationToken: cancellationToken);
            return new ConfigurationValidationSentinel(response.ETag);
        }
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
            requestOptions: new QueryRequestOptions { PartitionKey = ConfigurationPartition });
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
        using var iterator = container.GetItemQueryIterator<InvoiceConfigurationDocument>(
            query, requestOptions: new QueryRequestOptions { PartitionKey = ConfigurationPartition });
        var results = new List<StoredInvoiceConfiguration>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(page.Select(document =>
                new StoredInvoiceConfiguration(document.ToConfiguration(), document.ETag)));
        }
        return results;
    }

    private static PartitionKey ConfigurationPartition =>
        new(InvoiceConfigurationDocument.ConfigurationPartitionKey);

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

    /// <summary>
    /// Every outcome a normal caller can trigger (a duplicate ID, a stale document ETag, a lost
    /// sentinel race) is checked and translated to an <see cref="InvoiceConfigurationWriteResult"/>
    /// case before this is reached - reaching here means the batch failed for some other Cosmos
    /// reason (throttling, an unexpected status code), which is exactly the "environment/
    /// infrastructure failure" category docs/coding-standards.md reserves exceptions for.
    /// </summary>
    private static InvalidOperationException BatchFailed(TransactionalBatchResponse response) =>
        new($"Cosmos configuration transaction failed with {(int)response.StatusCode} {response.StatusCode}. {response.ErrorMessage}");
}
