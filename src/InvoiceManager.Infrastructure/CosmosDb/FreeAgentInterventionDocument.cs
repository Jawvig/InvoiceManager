using System.Globalization;
using System.Text.Json.Serialization;
using InvoiceManager.Core;
using NodaMoney;

namespace InvoiceManager.Infrastructure.CosmosDb;

/// <summary>
/// The Cosmos DB document shape for a FreeAgent Guess-removal intervention.
/// Maps between the Cosmos JSON structure and <see cref="FreeAgentGuessIntervention"/>.
/// </summary>
internal sealed class FreeAgentInterventionDocument
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("recordId")]
    public required string RecordId { get; init; }

    [JsonPropertyName("billUrl")]
    public required string BillUrl { get; init; }

    [JsonPropertyName("itemUrl")]
    public required string ItemUrl { get; init; }

    [JsonPropertyName("bankTransactionUrl")]
    public required string BankTransactionUrl { get; init; }

    [JsonPropertyName("guessExplanationUrl")]
    public required string GuessExplanationUrl { get; init; }

    [JsonPropertyName("currentBillAmount")]
    public required decimal CurrentBillAmount { get; init; }

    [JsonPropertyName("currentBillCurrency")]
    public required string CurrentBillCurrency { get; init; }

    [JsonPropertyName("proposedBillAmount")]
    public required decimal ProposedBillAmount { get; init; }

    [JsonPropertyName("proposedBillCurrency")]
    public required string ProposedBillCurrency { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("decidedAt")]
    public string? DecidedAt { get; init; }

    [JsonPropertyName("actorObjectId")]
    public string? ActorObjectId { get; init; }

    [JsonPropertyName("actorDisplayName")]
    public string? ActorDisplayName { get; init; }

    [JsonPropertyName("_etag")]
    public string ETag { get; init; } = "";

    public FreeAgentGuessIntervention ToIntervention() =>
        new(
            new FreeAgentInterventionId(Id),
            new InvoiceRecordId(RecordId),
            new Core.Integrations.FreeAgent.FreeAgentBillIdentity(BillUrl),
            new Core.Integrations.FreeAgent.FreeAgentBillItemIdentity(ItemUrl),
            BankTransactionUrl,
            GuessExplanationUrl,
            new Money(CurrentBillAmount, CurrentBillCurrency),
            new Money(ProposedBillAmount, ProposedBillCurrency),
            Reason,
            DateTimeOffset.ParseExact(CreatedAt, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            Enum.Parse<FreeAgentGuessInterventionStatus>(Status, ignoreCase: true));

    public static FreeAgentInterventionDocument FromIntervention(FreeAgentGuessIntervention intervention) => new()
    {
        Id = intervention.Id.Value,
        RecordId = intervention.RecordId.Value,
        BillUrl = intervention.Bill.Url.OriginalString,
        ItemUrl = intervention.Item.Url.OriginalString,
        BankTransactionUrl = intervention.BankTransactionUrl,
        GuessExplanationUrl = intervention.GuessExplanationUrl,
        CurrentBillAmount = intervention.CurrentBillAmount.Amount,
        CurrentBillCurrency = intervention.CurrentBillAmount.Currency.Code,
        ProposedBillAmount = intervention.ProposedBillAmount.Amount,
        ProposedBillCurrency = intervention.ProposedBillAmount.Currency.Code,
        Reason = intervention.Reason,
        CreatedAt = intervention.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
        Status = intervention.Status.ToString(),
    };
}
