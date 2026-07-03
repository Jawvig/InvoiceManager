using System.Text.Json.Serialization;
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

    public ActualInvoiceDetails ToDetails() =>
        new(DateOnly.ParseExact(ActualInvoiceDate, "yyyy-MM-dd"));

    public static ActualInvoiceDetailsDocument FromDetails(ActualInvoiceDetails details) =>
        new() { ActualInvoiceDate = details.ActualInvoiceDate.ToString("yyyy-MM-dd") };
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

    [JsonPropertyName("expectedAmount")]
    public required decimal ExpectedAmount { get; init; }

    [JsonPropertyName("expectedCurrency")]
    public required string ExpectedCurrency { get; init; }

    [JsonPropertyName("expectedVatMode")]
    public required string ExpectedVatMode { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("actualInvoiceDetails")]
    public ActualInvoiceDetailsDocument? ActualInvoiceDetails { get; init; }

    [JsonPropertyName("oneDriveDetails")]
    public OneDriveDetailsDocument? OneDriveDetails { get; init; }

    public InvoiceRecord ToRecord() =>
        new(
            new InvoiceConfigurationId(ConfigurationId),
            InvoiceDescription,
            DateOnly.ParseExact(ExpectedDate, "yyyy-MM-dd"),
            DateToleranceDays,
            new Money(ExpectedAmount, ExpectedCurrency),
            Enum.Parse<VatMode>(ExpectedVatMode, ignoreCase: true),
            ToState());

    public static InvoiceRecordDocument FromRecord(InvoiceRecord record)
    {
        var (status, actualDetails, oneDriveDetails) = StorageFields(record.State);
        return new InvoiceRecordDocument
        {
            Id = record.Id.Value,
            ConfigurationId = record.ConfigurationId.Value,
            InvoiceDescription = record.InvoiceDescription,
            ExpectedDate = record.ExpectedDate.ToString("yyyy-MM-dd"),
            DateToleranceDays = record.DateToleranceDays,
            ExpectedAmount = record.ExpectedAmount.Amount,
            ExpectedCurrency = record.ExpectedAmount.Currency.Code,
            ExpectedVatMode = record.ExpectedVatMode.ToString(),
            Status = status,
            ActualInvoiceDetails = actualDetails,
            OneDriveDetails = oneDriveDetails,
        };
    }

    private InvoiceWorkflowState ToState() => Status switch
    {
        nameof(Expected) => new Expected(),
        nameof(NotYetFound) => new NotYetFound(),
        nameof(NotFound) => new NotFound(),
        nameof(RetrievalError) => new RetrievalError(),
        nameof(Retrieved) => new Retrieved(RequiredActualDetails()),
        nameof(ReconciledFromOneDrive) => new ReconciledFromOneDrive(RequiredActualDetails(), RequiredOneDriveDetails()),
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

    private static (string Status, ActualInvoiceDetailsDocument? ActualDetails, OneDriveDetailsDocument? OneDriveDetails)
        StorageFields(InvoiceWorkflowState state) => state switch
        {
            Expected => (nameof(Expected), null, null),
            NotYetFound => (nameof(NotYetFound), null, null),
            NotFound => (nameof(NotFound), null, null),
            RetrievalError => (nameof(RetrievalError), null, null),
            Retrieved retrieved => (
                nameof(Retrieved),
                ActualInvoiceDetailsDocument.FromDetails(retrieved.ActualDetails),
                null),
            ReconciledFromOneDrive reconciled => (
                nameof(ReconciledFromOneDrive),
                ActualInvoiceDetailsDocument.FromDetails(reconciled.ActualDetails),
                OneDriveDetailsDocument.FromDetails(reconciled.OneDriveDetails)),
            SavedToOneDrive saved => (
                nameof(SavedToOneDrive),
                ActualInvoiceDetailsDocument.FromDetails(saved.ActualDetails),
                OneDriveDetailsDocument.FromDetails(saved.OneDriveDetails)),
        };
}
