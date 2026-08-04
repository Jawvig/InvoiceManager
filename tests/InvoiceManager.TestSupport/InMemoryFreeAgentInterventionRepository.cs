using InvoiceManager.Core;
using InvoiceManager.Core.Repositories;

namespace InvoiceManager.TestSupport;

public sealed class InMemoryFreeAgentInterventionRepository : IFreeAgentInterventionRepository
{
    private readonly Dictionary<string, FreeAgentGuessIntervention> store = [];

    public IReadOnlyList<FreeAgentGuessIntervention> Created => store.Values.ToList();

    public Task<FreeAgentGuessIntervention> CreateAsync(
        FreeAgentGuessIntervention intervention, CancellationToken cancellationToken = default)
    {
        store[intervention.Id.Value] = intervention;
        return Task.FromResult(intervention);
    }

    public Task<IReadOnlyList<FreeAgentGuessIntervention>> ListPendingAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FreeAgentGuessIntervention>>(
            store.Values.Where(i => i.Status == FreeAgentGuessInterventionStatus.Pending).ToList());

    public Task<Option<FreeAgentGuessIntervention>> GetAsync(
        FreeAgentInterventionId id, CancellationToken cancellationToken = default)
    {
        Option<FreeAgentGuessIntervention> result = store.TryGetValue(id.Value, out var intervention)
            ? intervention
            : Option.None;
        return Task.FromResult(result);
    }

    public Task<FreeAgentInterventionDecisionResult> RecordDecisionAsync(
        FreeAgentGuessInterventionDecision decision, CancellationToken cancellationToken = default)
    {
        if (!store.TryGetValue(decision.InterventionId.Value, out var existing) ||
            existing.Status != FreeAgentGuessInterventionStatus.Pending)
        {
            return Task.FromResult<FreeAgentInterventionDecisionResult>(new FreeAgentInterventionAlreadyDecided());
        }

        var updated = existing with { Status = decision.Decision };
        store[decision.InterventionId.Value] = updated;
        return Task.FromResult<FreeAgentInterventionDecisionResult>(updated);
    }

    public Task<bool> HasPendingInterventionAsync(InvoiceRecordId recordId, CancellationToken cancellationToken = default) =>
        Task.FromResult(store.Values.Any(i =>
            i.RecordId == recordId && i.Status == FreeAgentGuessInterventionStatus.Pending));
}
