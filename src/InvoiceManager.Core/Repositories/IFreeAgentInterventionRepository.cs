namespace InvoiceManager.Core.Repositories;

/// <summary>The intervention was already decided; the caller's decision was not recorded.</summary>
public sealed record FreeAgentInterventionAlreadyDecided;

/// <summary>The outcome of recording a decision against a Guess-removal intervention.</summary>
public union FreeAgentInterventionDecisionResult(FreeAgentGuessIntervention, FreeAgentInterventionAlreadyDecided);

/// <summary>
/// Persists FreeAgent Guess-removal interventions. This is the seam the deferred
/// AdminWeb approval UI (a separate, later PR) builds against - no further Core
/// changes are needed for that page to list pending interventions and record a
/// decision.
/// </summary>
public interface IFreeAgentInterventionRepository
{
    Task<FreeAgentGuessIntervention> CreateAsync(
        FreeAgentGuessIntervention intervention, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FreeAgentGuessIntervention>> ListPendingAsync(CancellationToken cancellationToken = default);

    Task<Option<FreeAgentGuessIntervention>> GetAsync(
        FreeAgentInterventionId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the decision, conditioned on the intervention still being <see cref="FreeAgentGuessInterventionStatus.Pending"/>.
    /// </summary>
    Task<FreeAgentInterventionDecisionResult> RecordDecisionAsync(
        FreeAgentGuessInterventionDecision decision, CancellationToken cancellationToken = default);

    /// <summary>Whether a pending (undecided) intervention already exists for the given record - used to skip re-entering the FreeAgent stage while one is outstanding.</summary>
    Task<bool> HasPendingInterventionAsync(InvoiceRecordId recordId, CancellationToken cancellationToken = default);
}
