using System.Net;
using InvoiceManager.Core;
using InvoiceManager.Core.Repositories;
using Microsoft.Azure.Cosmos;

namespace InvoiceManager.Infrastructure.CosmosDb;

/// <summary>
/// Cosmos DB implementation of <see cref="IFreeAgentInterventionRepository"/>.
/// Container: <c>freeagent-interventions</c>, partition key: <c>/recordId</c>.
/// </summary>
public sealed class CosmosFreeAgentInterventionRepository : IFreeAgentInterventionRepository
{
    private readonly Container container;

    public CosmosFreeAgentInterventionRepository(CosmosClient cosmosClient, string databaseName)
    {
        container = cosmosClient.GetContainer(databaseName, CosmosSchema.FreeAgentInterventions.Name);
    }

    public async Task<FreeAgentGuessIntervention> CreateAsync(
        FreeAgentGuessIntervention intervention, CancellationToken cancellationToken = default)
    {
        var document = FreeAgentInterventionDocument.FromIntervention(intervention);
        var partitionKey = new PartitionKey(intervention.RecordId.Value);
        await container.CreateItemAsync(document, partitionKey, cancellationToken: cancellationToken);
        return intervention;
    }

    public async Task<IReadOnlyList<FreeAgentGuessIntervention>> ListPendingAsync(
        CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.status = @pendingStatus")
            .WithParameter("@pendingStatus", nameof(FreeAgentGuessInterventionStatus.Pending));

        using var iterator = container.GetItemQueryIterator<FreeAgentInterventionDocument>(query);

        var interventions = new List<FreeAgentGuessIntervention>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            foreach (var document in page)
                interventions.Add(document.ToIntervention());
        }

        return interventions;
    }

    public async Task<Option<FreeAgentGuessIntervention>> GetAsync(
        FreeAgentInterventionId id, CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @id")
            .WithParameter("@id", id.Value);

        using var iterator = container.GetItemQueryIterator<FreeAgentInterventionDocument>(query);

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            foreach (var document in page)
                return document.ToIntervention();
        }

        return Option.None;
    }

    public async Task<FreeAgentInterventionDecisionResult> RecordDecisionAsync(
        FreeAgentGuessInterventionDecision decision, CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @id")
            .WithParameter("@id", decision.InterventionId.Value);

        using var iterator = container.GetItemQueryIterator<FreeAgentInterventionDocument>(query);

        FreeAgentInterventionDocument? existing = null;
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            existing = page.FirstOrDefault();
            if (existing is not null)
                break;
        }

        if (existing is null || existing.Status != nameof(FreeAgentGuessInterventionStatus.Pending))
            return new FreeAgentInterventionAlreadyDecided();

        var updated = new FreeAgentInterventionDocument
        {
            Id = existing.Id,
            RecordId = existing.RecordId,
            BillUrl = existing.BillUrl,
            ItemUrl = existing.ItemUrl,
            BankTransactionUrl = existing.BankTransactionUrl,
            GuessExplanationUrl = existing.GuessExplanationUrl,
            CurrentBillAmount = existing.CurrentBillAmount,
            CurrentBillCurrency = existing.CurrentBillCurrency,
            ProposedBillAmount = existing.ProposedBillAmount,
            ProposedBillCurrency = existing.ProposedBillCurrency,
            Reason = existing.Reason,
            CreatedAt = existing.CreatedAt,
            Status = decision.Decision.ToString(),
            DecidedAt = decision.DecidedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ActorObjectId = decision.ActorObjectId,
            ActorDisplayName = decision.ActorDisplayName,
        };

        try
        {
            var response = await container.ReplaceItemAsync(
                updated,
                updated.Id,
                new PartitionKey(updated.RecordId),
                new ItemRequestOptions { IfMatchEtag = existing.ETag },
                cancellationToken);
            return response.Resource.ToIntervention();
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            // Someone else recorded a decision between our read and write.
            return new FreeAgentInterventionAlreadyDecided();
        }
    }

    public async Task<bool> HasPendingInterventionAsync(
        InvoiceRecordId recordId, CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition(
            "SELECT VALUE COUNT(1) FROM c WHERE c.recordId = @recordId AND c.status = @pendingStatus")
            .WithParameter("@recordId", recordId.Value)
            .WithParameter("@pendingStatus", nameof(FreeAgentGuessInterventionStatus.Pending));

        using var iterator = container.GetItemQueryIterator<int>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(recordId.Value) });

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            foreach (var count in page)
                return count > 0;
        }

        return false;
    }
}
