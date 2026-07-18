using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.Json;
using InvoiceManager.Core;
using NodaMoney;

namespace InvoiceManager.Infrastructure.CosmosDb;

/// <summary>
/// The Cosmos DB document shape for an invoice configuration.
/// Maps between the Cosmos JSON structure and <see cref="InvoiceConfiguration"/>.
/// </summary>
internal sealed class InvoiceConfigurationDocument
{
    public const string LiveDocumentType = "invoiceConfiguration";

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("documentType")]
    public string DocumentType { get; init; } = LiveDocumentType;

    [JsonPropertyName("_etag")]
    public string ETag { get; init; } = "";

    [JsonPropertyName("integrationType")]
    public required string IntegrationType { get; init; }

    [JsonPropertyName("invoiceDescription")]
    public required string InvoiceDescription { get; init; }

    [JsonPropertyName("frequency")]
    public required string Frequency { get; init; }

    [JsonPropertyName("amountMatchingCriteria")]
    public AmountMatchingCriteriaDocument? AmountMatchingCriteria { get; init; }

    [JsonPropertyName("defaultVatMode")]
    public required string DefaultVatMode { get; init; }

    [JsonPropertyName("isActive")]
    public required bool IsActive { get; init; }

    [JsonPropertyName("oneDriveDestination")]
    public required JsonElement OneDriveDestination { get; init; }

    [JsonPropertyName("startDate")]
    public required string StartDate { get; init; }

    [JsonPropertyName("billingAccountId")]
    public required string BillingAccountId { get; init; }

    [JsonPropertyName("dateToleranceDays")]
    public required int DateToleranceDays { get; init; }

    /// <summary>For <c>Microsoft365Email</c>, the sender a candidate email must come from. Empty for other types.</summary>
    [JsonPropertyName("senderEmailAddress")]
    public string SenderEmailAddress { get; init; } = "";

    /// <summary>For <c>Microsoft365Email</c>, a regex a candidate email's plain-text body must match. Empty for other types.</summary>
    [JsonPropertyName("bodyPattern")]
    public string BodyPattern { get; init; } = "";

    public InvoiceConfiguration ToConfiguration() =>
        new(
            new InvoiceConfigurationId(Id),
            Enum.Parse<Core.IntegrationType>(IntegrationType, ignoreCase: true),
            InvoiceDescription,
            Enum.Parse<InvoiceFrequency>(Frequency, ignoreCase: true),
            ToAmountMatchingCriteria(),
            Enum.Parse<VatMode>(DefaultVatMode, ignoreCase: true),
            IsActive,
            ToOneDriveDestination(),
            DateOnly.ParseExact(StartDate, "O", CultureInfo.InvariantCulture),
            BillingAccountId,
            DateToleranceDays,
            SenderEmailAddress,
            BodyPattern);

    private Option<AmountMatchingCriteria> ToAmountMatchingCriteria() =>
        AmountMatchingCriteria is { } criteria
            ? criteria.ToCriteria()
            : Option.None;

    private OneDriveDestination ToOneDriveDestination()
    {
        if (OneDriveDestination.ValueKind == JsonValueKind.String)
            return new(OneDriveDestination.GetString() ?? string.Empty);

        var destination = OneDriveDestination.Deserialize<OneDriveDestinationDocument>()
            ?? throw new InvalidOperationException($"Configuration '{Id}' has an invalid OneDrive destination.");
        return destination.ToDestination();
    }

    public static InvoiceConfigurationDocument FromConfiguration(InvoiceConfiguration config) =>
        new()
        {
            Id = config.Id.Value,
            IntegrationType = config.IntegrationType.ToString(),
            InvoiceDescription = config.InvoiceDescription,
            Frequency = config.Frequency.ToString(),
            AmountMatchingCriteria = config.AmountMatchingCriteria switch
            {
                AmountMatchingCriteria criteria => AmountMatchingCriteriaDocument.FromCriteria(criteria),
                None => null,
            },
            DefaultVatMode = config.DefaultVatMode.ToString(),
            IsActive = config.IsActive,
            OneDriveDestination = config.OneDriveDestination.IsLegacyPath
                ? JsonSerializer.SerializeToElement(config.OneDriveDestination.DisplayPath)
                : JsonSerializer.SerializeToElement(OneDriveDestinationDocument.FromDestination(config.OneDriveDestination)),
            StartDate = config.StartDate.ToString("O", CultureInfo.InvariantCulture),
            BillingAccountId = config.BillingAccountId,
            DateToleranceDays = config.DateToleranceDays,
            SenderEmailAddress = config.SenderEmailAddress,
            BodyPattern = config.BodyPattern,
        };
}

internal sealed class OneDriveDestinationDocument
{
    [JsonPropertyName("driveId")]
    public required string DriveId { get; init; }

    [JsonPropertyName("folderItemId")]
    public required string FolderItemId { get; init; }

    [JsonPropertyName("displayPath")]
    public required string DisplayPath { get; init; }

    public OneDriveDestination ToDestination() => new(DisplayPath, DriveId, FolderItemId);

    public static OneDriveDestinationDocument FromDestination(OneDriveDestination destination) => new()
    {
        DriveId = destination.DriveId!,
        FolderItemId = destination.FolderItemId!,
        DisplayPath = destination.DisplayPath,
    };
}

internal sealed class InvoiceConfigurationRevisionDocument
{
    public const string RevisionDocumentType = "invoiceConfigurationRevision";

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("documentType")]
    public string DocumentType { get; init; } = RevisionDocumentType;

    [JsonPropertyName("configurationId")]
    public required string ConfigurationId { get; init; }

    [JsonPropertyName("integrationType")]
    public required string IntegrationType { get; init; }

    [JsonPropertyName("action")]
    public required string Action { get; init; }

    [JsonPropertyName("timestamp")]
    public required string Timestamp { get; init; }

    [JsonPropertyName("actorObjectId")]
    public string? ActorObjectId { get; init; }

    [JsonPropertyName("actorDisplayName")]
    public required string ActorDisplayName { get; init; }

    [JsonPropertyName("snapshot")]
    public required InvoiceConfigurationDocument Snapshot { get; init; }

    public InvoiceConfigurationRevision ToRevision() => new(
        Id,
        new(ConfigurationId),
        Enum.Parse<IntegrationType>(IntegrationType, true),
        Enum.Parse<InvoiceConfigurationRevisionAction>(Action, true),
        DateTimeOffset.ParseExact(Timestamp, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        ActorObjectId,
        ActorDisplayName,
        Snapshot.ToConfiguration());

    public static InvoiceConfigurationRevisionDocument FromRevision(InvoiceConfigurationRevision revision) => new()
    {
        Id = revision.RevisionId,
        ConfigurationId = revision.ConfigurationId.Value,
        IntegrationType = revision.IntegrationType.ToString(),
        Action = revision.Action.ToString(),
        Timestamp = revision.Timestamp.ToString("O", CultureInfo.InvariantCulture),
        ActorObjectId = revision.ActorObjectId,
        ActorDisplayName = revision.ActorDisplayName,
        Snapshot = InvoiceConfigurationDocument.FromConfiguration(revision.Snapshot),
    };
}

internal sealed class AmountMatchingCriteriaDocument
{
    [JsonPropertyName("amount")]
    public required decimal Amount { get; init; }

    [JsonPropertyName("currency")]
    public required string Currency { get; init; }

    [JsonPropertyName("amountTolerance")]
    public required decimal AmountTolerance { get; init; }

    public AmountMatchingCriteria ToCriteria() =>
        new(new Money(Amount, Currency), AmountTolerance);

    public static AmountMatchingCriteriaDocument FromCriteria(AmountMatchingCriteria criteria) => new()
    {
        Amount = criteria.Amount.Amount,
        Currency = criteria.Amount.Currency.Code,
        AmountTolerance = criteria.AmountTolerance,
    };
}
