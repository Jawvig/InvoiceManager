using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.Json;
using InvoiceManager.Core;
using NodaMoney;

namespace InvoiceManager.Infrastructure.CosmosDb;

/// <summary>
/// The Cosmos JSON shape for <see cref="ActualInvoiceDetails"/>. The sub-object
/// is present only when the record's state carries actual invoice values; when
/// present, every property is required.
/// </summary>
internal sealed class ActualInvoiceDetailsDocument
{
    [JsonPropertyName("actualInvoiceDate")]
    public required string ActualInvoiceDate { get; init; }

    [JsonPropertyName("actualAmount")]
    public required decimal ActualAmount { get; init; }

    [JsonPropertyName("actualCurrency")]
    public required string ActualCurrency { get; init; }

    [JsonPropertyName("sourceInvoiceId")]
    public required string SourceInvoiceId { get; init; }

    public ActualInvoiceDetails ToDetails() =>
        new(
            DateOnly.ParseExact(ActualInvoiceDate, "O", CultureInfo.InvariantCulture),
            new Money(ActualAmount, ActualCurrency),
            new Core.SourceInvoiceId(SourceInvoiceId));

    public static ActualInvoiceDetailsDocument FromDetails(ActualInvoiceDetails details) =>
        new()
        {
            ActualInvoiceDate = details.ActualInvoiceDate.ToString("O", CultureInfo.InvariantCulture),
            ActualAmount = details.ActualAmount.Amount,
            ActualCurrency = details.ActualAmount.Currency.Code,
            SourceInvoiceId = details.SourceInvoiceId.Value,
        };
}

/// <summary>
/// The Cosmos JSON shape for <see cref="OneDriveDetails"/>. The sub-object is
/// present only when the record's state carries a OneDrive location; when
/// present, every property is required.
/// </summary>
internal sealed class OneDriveDetailsDocument
{
    [JsonPropertyName("oneDriveLocation")]
    public required string OneDriveLocation { get; init; }

    public OneDriveDetails ToDetails() => new(OneDriveLocation);

    public static OneDriveDetailsDocument FromDetails(OneDriveDetails details) =>
        new() { OneDriveLocation = details.OneDriveLocation };
}

/// <summary>
/// The Cosmos DB document shape for an invoice record.
/// Maps between the Cosmos JSON structure and <see cref="InvoiceRecord"/>.
/// The <c>status</c> string discriminates the workflow state; the nested
/// detail sub-objects are present exactly when the state requires them.
/// </summary>
internal sealed class InvoiceRecordDocument
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("configurationId")]
    public required string ConfigurationId { get; init; }

    [JsonPropertyName("invoiceDescription")]
    public required string InvoiceDescription { get; init; }

    [JsonPropertyName("expectedDate")]
    public required string ExpectedDate { get; init; }

    [JsonPropertyName("dateToleranceDays")]
    public required int DateToleranceDays { get; init; }

    [JsonPropertyName("amountMatchingCriteria")]
    public AmountMatchingCriteriaDocument? AmountMatchingCriteria { get; init; }

    [JsonPropertyName("expectedVatMode")]
    public required string ExpectedVatMode { get; init; }

    [JsonPropertyName("processingSnapshot")]
    public InvoiceProcessingSnapshotDocument? ProcessingSnapshot { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("actualInvoiceDetails")]
    public ActualInvoiceDetailsDocument? ActualInvoiceDetails { get; init; }

    [JsonPropertyName("oneDriveDetails")]
    public OneDriveDetailsDocument? OneDriveDetails { get; init; }

    // Present only for the RetrievalError state: the technical failure detail.
    [JsonPropertyName("lastError")]
    public string? LastError { get; init; }

    // Present only for the ReconciledFromOneDrive state: why the existing file was
    // accepted and when reconciliation occurred (ISO 8601 round-trip).
    [JsonPropertyName("matchReason")]
    public string? MatchReason { get; init; }

    [JsonPropertyName("reconciledAt")]
    public string? ReconciledAt { get; init; }

    public InvoiceRecord ToRecord() =>
        new(
            new InvoiceConfigurationId(ConfigurationId),
            InvoiceDescription,
            DateOnly.ParseExact(ExpectedDate, "O", CultureInfo.InvariantCulture),
            DateToleranceDays,
            ToAmountMatchingCriteria(),
            Enum.Parse<VatMode>(ExpectedVatMode, ignoreCase: true),
            ToState(),
            ProcessingSnapshot?.ToSnapshot());

    public static InvoiceRecordDocument FromRecord(InvoiceRecord record)
    {
        var fields = StorageFields(record.State);
        return new InvoiceRecordDocument
        {
            Id = record.Id.Value,
            ConfigurationId = record.ConfigurationId.Value,
            InvoiceDescription = record.InvoiceDescription,
            ExpectedDate = record.ExpectedDate.ToString("O", CultureInfo.InvariantCulture),
            DateToleranceDays = record.DateToleranceDays,
            AmountMatchingCriteria = record.AmountMatchingCriteria switch
            {
                AmountMatchingCriteria criteria => AmountMatchingCriteriaDocument.FromCriteria(criteria),
                None => null,
            },
            ExpectedVatMode = record.ExpectedVatMode.ToString(),
            ProcessingSnapshot = record.ProcessingSnapshot is { } snapshot
                ? InvoiceProcessingSnapshotDocument.FromSnapshot(snapshot)
                : null,
            Status = fields.Status,
            ActualInvoiceDetails = fields.ActualDetails,
            OneDriveDetails = fields.OneDriveDetails,
            LastError = fields.LastError,
            MatchReason = fields.MatchReason,
            ReconciledAt = fields.ReconciledAt,
        };
    }

    private Option<AmountMatchingCriteria> ToAmountMatchingCriteria() =>
        AmountMatchingCriteria is { } criteria
            ? criteria.ToCriteria()
            : Option.None;

    private InvoiceWorkflowState ToState() => Status switch
    {
        nameof(Expected) => new Expected(),
        nameof(NotFound) => new NotFound(),
        nameof(RetrievalError) => new RetrievalError(LastError ?? string.Empty),
        nameof(Retrieved) => new Retrieved(RequiredActualDetails()),
        nameof(ReconciledFromOneDrive) => new ReconciledFromOneDrive(
            RequiredActualDetails(),
            RequiredOneDriveDetails(),
            RequiredMatchReason(),
            RequiredReconciledAt()),
        nameof(SavedToOneDrive) => new SavedToOneDrive(RequiredActualDetails(), RequiredOneDriveDetails()),
        _ => throw new InvalidOperationException(
            $"Invoice record document '{Id}' has unrecognised status '{Status}'."),
    };

    private ActualInvoiceDetails RequiredActualDetails() =>
        ActualInvoiceDetails?.ToDetails()
        ?? throw new InvalidOperationException(
            $"Invoice record document '{Id}' has status '{Status}' but is missing 'actualInvoiceDetails'.");

    private OneDriveDetails RequiredOneDriveDetails() =>
        OneDriveDetails?.ToDetails()
        ?? throw new InvalidOperationException(
            $"Invoice record document '{Id}' has status '{Status}' but is missing 'oneDriveDetails'.");

    private string RequiredMatchReason() =>
        MatchReason
        ?? throw new InvalidOperationException(
            $"Invoice record document '{Id}' has status '{Status}' but is missing 'matchReason'.");

    private DateTimeOffset RequiredReconciledAt() =>
        ReconciledAt is { } value
            ? DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            : throw new InvalidOperationException(
                $"Invoice record document '{Id}' has status '{Status}' but is missing 'reconciledAt'.");

    private static StorageFieldSet StorageFields(InvoiceWorkflowState state) => state switch
    {
        Expected => new(nameof(Expected), null, null, null, null, null),
        NotFound => new(nameof(NotFound), null, null, null, null, null),
        RetrievalError error => new(nameof(RetrievalError), null, null, error.ErrorMessage, null, null),
        Retrieved retrieved => new(
            nameof(Retrieved),
            ActualInvoiceDetailsDocument.FromDetails(retrieved.ActualDetails),
            null,
            null,
            null,
            null),
        ReconciledFromOneDrive reconciled => new(
            nameof(ReconciledFromOneDrive),
            ActualInvoiceDetailsDocument.FromDetails(reconciled.ActualDetails),
            OneDriveDetailsDocument.FromDetails(reconciled.OneDriveDetails),
            null,
            reconciled.MatchReason,
            reconciled.ReconciledAt.ToString("O", CultureInfo.InvariantCulture)),
        SavedToOneDrive saved => new(
            nameof(SavedToOneDrive),
            ActualInvoiceDetailsDocument.FromDetails(saved.ActualDetails),
            OneDriveDetailsDocument.FromDetails(saved.OneDriveDetails),
            null,
            null,
            null),
    };

    private readonly record struct StorageFieldSet(
        string Status,
        ActualInvoiceDetailsDocument? ActualDetails,
        OneDriveDetailsDocument? OneDriveDetails,
        string? LastError,
        string? MatchReason,
        string? ReconciledAt);
}

internal sealed class InvoiceProcessingSnapshotDocument
{
    [JsonPropertyName("integrationType")]
    public required string IntegrationType { get; init; }

    [JsonPropertyName("billingAccountId")]
    public required string BillingAccountId { get; init; }

    [JsonPropertyName("oneDriveDestination")]
    public required JsonElement OneDriveDestination { get; init; }

    [JsonPropertyName("invoiceDescription")]
    public required string InvoiceDescription { get; init; }

    [JsonPropertyName("dateToleranceDays")]
    public required int DateToleranceDays { get; init; }

    [JsonPropertyName("amountMatchingCriteria")]
    public AmountMatchingCriteriaDocument? AmountMatchingCriteria { get; init; }

    [JsonPropertyName("vatMode")]
    public required string VatMode { get; init; }

    [JsonPropertyName("senderEmailAddress")]
    public string SenderEmailAddress { get; init; } = "";

    [JsonPropertyName("bodyPattern")]
    public string BodyPattern { get; init; } = "";

    public InvoiceProcessingSnapshot ToSnapshot() => new(
        Enum.Parse<IntegrationType>(IntegrationType, true),
        BillingAccountId,
        ToDestination(),
        InvoiceDescription,
        DateToleranceDays,
        AmountMatchingCriteria is { } criteria ? criteria.ToCriteria() : Option.None,
        Enum.Parse<VatMode>(VatMode, true),
        SenderEmailAddress,
        BodyPattern);

    public static InvoiceProcessingSnapshotDocument FromSnapshot(InvoiceProcessingSnapshot snapshot) => new()
    {
        IntegrationType = snapshot.IntegrationType.ToString(),
        BillingAccountId = snapshot.BillingAccountId,
        OneDriveDestination = snapshot.OneDriveDestination.IsLegacyPath
            ? JsonSerializer.SerializeToElement(snapshot.OneDriveDestination.DisplayPath)
            : JsonSerializer.SerializeToElement(OneDriveDestinationDocument.FromDestination(snapshot.OneDriveDestination)),
        InvoiceDescription = snapshot.InvoiceDescription,
        DateToleranceDays = snapshot.DateToleranceDays,
        AmountMatchingCriteria = snapshot.AmountMatchingCriteria switch
        {
            AmountMatchingCriteria criteria => AmountMatchingCriteriaDocument.FromCriteria(criteria),
            None => null,
        },
        VatMode = snapshot.VatMode.ToString(),
        SenderEmailAddress = snapshot.SenderEmailAddress,
        BodyPattern = snapshot.BodyPattern,
    };

    private OneDriveDestination ToDestination() => OneDriveDestination.ValueKind == JsonValueKind.String
        ? new(OneDriveDestination.GetString() ?? string.Empty)
        : OneDriveDestination.Deserialize<OneDriveDestinationDocument>()?.ToDestination()
          ?? throw new InvalidOperationException("Invoice record has an invalid OneDrive destination snapshot.");
}
