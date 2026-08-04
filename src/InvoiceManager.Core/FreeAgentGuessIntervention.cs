using System.ComponentModel;
using System.Text.Json.Serialization;
using InvoiceManager.Core.Integrations.FreeAgent;
using NodaMoney;

namespace InvoiceManager.Core;

/// <summary>The stable ID of a FreeAgent Guess-removal intervention record.</summary>
[TypeConverter(typeof(StringIdTypeConverter<FreeAgentInterventionId>))]
[JsonConverter(typeof(StringIdJsonConverter<FreeAgentInterventionId>))]
public sealed record FreeAgentInterventionId(string Value) : IStringId<FreeAgentInterventionId>
{
    public string Value { get; } = !string.IsNullOrEmpty(Value)
        ? Value
        : throw new ArgumentException("FreeAgentInterventionId cannot be null or empty.", nameof(Value));

    public override string ToString() => Value;

    static FreeAgentInterventionId IStringId<FreeAgentInterventionId>.Create(string value) => new(value);
}

/// <summary>The lifecycle status of a Guess-removal intervention.</summary>
public enum FreeAgentGuessInterventionStatus
{
    Pending,
    Approved,
    Declined,
    Expired,
}

/// <summary>
/// Everything an administrator needs to decide whether to remove a matched Bill
/// Payment Guess, and everything needed to audit the decision. Immutable once
/// created except for the status transition captured by
/// <see cref="FreeAgentGuessInterventionDecision"/> - mirrors how
/// <c>InvoiceConfigurationRevision</c> makes every configuration edit its own
/// append-only document.
/// </summary>
public sealed record FreeAgentGuessIntervention(
    FreeAgentInterventionId Id,
    InvoiceRecordId RecordId,
    FreeAgentBillIdentity Bill,
    FreeAgentBillItemIdentity Item,
    string BankTransactionUrl,
    string GuessExplanationUrl,
    Money CurrentBillAmount,
    Money ProposedBillAmount,
    string Reason,
    DateTimeOffset CreatedAt,
    FreeAgentGuessInterventionStatus Status)
{
    public static FreeAgentGuessIntervention Create(
        FreeAgentInterventionId id,
        InvoiceRecordId recordId,
        FreeAgentPaymentInterventionDetails details,
        DateTimeOffset createdAt) =>
        new(
            id,
            recordId,
            details.Bill,
            details.Item,
            details.BankTransactionUrl,
            details.GuessExplanationUrl,
            details.CurrentBillAmount,
            details.ProposedBillAmount,
            details.Reason,
            createdAt,
            FreeAgentGuessInterventionStatus.Pending);
}

/// <summary>An administrator's (or expiry's) decision on a pending intervention, appended rather than mutating the original record.</summary>
public sealed record FreeAgentGuessInterventionDecision(
    FreeAgentInterventionId InterventionId,
    FreeAgentGuessInterventionStatus Decision,
    string ActorObjectId,
    string ActorDisplayName,
    DateTimeOffset DecidedAt);
