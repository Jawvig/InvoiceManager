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
    /// Bootstrap seeding is insert-only and never overwrites UI-managed values - if a
    /// configuration with the same ID already exists, this is <b>always</b> a safe no-op,
    /// regardless of how its live search criteria may have since drifted from what the seed file
    /// originally specified (e.g. an admin edited it after it was first seeded, freeing up its
    /// original criteria for some other configuration to legitimately claim) - re-seeding an
    /// already-existing ID must never fail or change anything. Only when no configuration with
    /// this ID exists yet does the insert proceed, and it also advances the duplicate-validation
    /// sentinel in the same transactional batch (see <see cref="ConfigurationValidationSentinel"/>
    /// and docs/data-model.md): deploys run Terraform apply (which can start routing traffic to a
    /// live AdminWeb instance) before invoking the seeder, so a seeded configuration can genuinely
    /// race a concurrent Create/Update/Restore request through AdminWeb - see
    /// <c>scripts/Deploy-Infra.ps1</c>'s <c>Invoke-ConfigurationSeeder</c> call sites, both of which
    /// run after <c>terraform apply</c>/an unchanged-plan early return, not before AdminWeb comes
    /// up.
    ///
    /// <para>
    /// Every insert attempt (not just the first) revalidates <paramref name="configuration"/>'s
    /// search criteria against the current live list before inserting - covering both race
    /// orderings, not just the one where the seeder wins first: if an AdminWeb write already
    /// committed conflicting criteria before this method's first insert attempt, or commits them
    /// between this method losing a sentinel race and its retry, either way the retry must catch
    /// it rather than blindly re-attempting the same insert on top of a now-conflicting live
    /// configuration. A seed-time conflict throws <see cref="SeedConfigurationConflictException"/>
    /// instead of being silently skipped - see that type's XML doc for why. This check only ever
    /// runs once the existing-ID no-op above has ruled out that this is just an idempotent
    /// re-seed, so it can never fire for the drifted-criteria scenario described above.
    /// </para>
    /// </summary>
    public async Task CreateIfNotExistsAsync(
        InvoiceConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        if (await ExistsAsync(configuration.Id, cancellationToken))
            return; // Already seeded - insert-only, always a safe no-op, live criteria notwithstanding.

        var document = InvoiceConfigurationDocument.FromConfiguration(configuration);
        const int documentIndex = 0;
        const int sentinelIndex = 1;
        const int maxAttempts = 5;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var sentinel = await GetValidationSentinelAsync(cancellationToken);

            var others = (await ListAllAsync(cancellationToken)).Select(x => x.Configuration).ToList();
            if (InvoiceConfigurationValidation.ValidateNoDuplicateMatch(configuration, others) is InvoiceConfigurationId conflictingId)
                throw new SeedConfigurationConflictException(
                    $"Seed configuration '{configuration.Id}' has the same search criteria as " +
                    $"existing configuration '{conflictingId}'. Fix the seed data or the " +
                    "conflicting configuration, then re-run the seeder.");

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
                return; // Lost a genuine concurrent-create race for this same new ID - also a safe no-op.

            if (response[sentinelIndex].StatusCode == HttpStatusCode.PreconditionFailed && attempt < maxAttempts)
                continue; // Another writer advanced the sentinel first - revalidate and retry the insert.

            throw BatchFailed(response);
        }
    }

    /// <summary>
    /// Whether a live configuration document with this exact ID already exists, regardless of its
    /// integration type or current search criteria - used only to decide whether
    /// <see cref="CreateIfNotExistsAsync"/>'s insert-or-no-op branch applies. Deliberately not
    /// <see cref="GetAsync"/> (which also needs to match an <c>integrationType</c>): the live
    /// document's Cosmos <c>id</c> is the configuration ID itself, so a plain point-read is
    /// enough, and this must not require the caller to already know the integration type.
    /// </summary>
    private async Task<bool> ExistsAsync(InvoiceConfigurationId id, CancellationToken cancellationToken)
    {
        try
        {
            await container.ReadItemAsync<InvoiceConfigurationDocument>(
                id.Value, ConfigurationPartition, cancellationToken: cancellationToken);
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
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
