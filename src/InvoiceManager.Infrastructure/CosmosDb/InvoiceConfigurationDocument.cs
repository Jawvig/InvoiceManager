using System.Globalization;
using System.Text.Json.Serialization;
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
    public const string ConfigurationPartitionKey = "config";

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("documentType")]
    public string DocumentType { get; init; } = LiveDocumentType;

    [JsonPropertyName("partitionKey")]
    public string PartitionKey { get; init; } = ConfigurationPartitionKey;

    [JsonPropertyName("_etag")]
    public string ETag { get; init; } = "";

    /// <summary>
    /// Retained for Cosmos query/index filtering. Written from the configuration's
    /// (derived) <see cref="Core.IntegrationType"/> on save, but not read back on
    /// load — the integration type is instead derived from <see cref="IntegrationConfiguration"/>.
    /// </summary>
    [JsonPropertyName("integrationType")]
    public required string IntegrationType { get; init; }

    [JsonPropertyName("integrationConfiguration")]
    public required IntegrationConfigurationDocument IntegrationConfiguration { get; init; }

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

    [JsonPropertyName("oneDriveFolder")]
    public required OneDriveFolderDocument OneDriveFolder { get; init; }

    [JsonPropertyName("startDate")]
    public required string StartDate { get; init; }

    [JsonPropertyName("dateToleranceDays")]
    public required int DateToleranceDays { get; init; }

    public InvoiceConfiguration ToConfiguration() =>
        new(
            new InvoiceConfigurationId(Id),
            IntegrationConfiguration.ToConfiguration(),
            InvoiceDescription,
            Enum.Parse<InvoiceFrequency>(Frequency, ignoreCase: true),
            ToAmountMatchingCriteria(),
            Enum.Parse<VatMode>(DefaultVatMode, ignoreCase: true),
            IsActive,
            OneDriveFolder.ToFolder(),
            DateOnly.ParseExact(StartDate, "O", CultureInfo.InvariantCulture),
            DateToleranceDays);

    private Option<AmountMatchingCriteria> ToAmountMatchingCriteria() =>
        AmountMatchingCriteria is { } criteria
            ? criteria.ToCriteria()
            : Option.None;

    public static InvoiceConfigurationDocument FromConfiguration(InvoiceConfiguration config) =>
        new()
        {
            Id = config.Id.Value,
            IntegrationType = config.IntegrationType.ToString(),
            IntegrationConfiguration = IntegrationConfigurationDocument.FromConfiguration(config.IntegrationConfiguration),
            InvoiceDescription = config.InvoiceDescription,
            Frequency = config.Frequency.ToString(),
            AmountMatchingCriteria = config.AmountMatchingCriteria switch
            {
                AmountMatchingCriteria criteria => AmountMatchingCriteriaDocument.FromCriteria(criteria),
                None => null,
            },
            DefaultVatMode = config.DefaultVatMode.ToString(),
            IsActive = config.IsActive,
            OneDriveFolder = OneDriveFolderDocument.FromFolder(config.OneDriveFolder),
            StartDate = config.StartDate.ToString("O", CultureInfo.InvariantCulture),
            DateToleranceDays = config.DateToleranceDays,
        };
}

/// <summary>
/// The discriminated JSON shape for <see cref="Core.IntegrationConfiguration"/>. The
/// <see cref="Type"/> discriminator selects which of the other (nullable) fields apply.
/// </summary>
internal sealed class IntegrationConfigurationDocument
{
    private const string MicrosoftBillingType = "microsoftBilling";
    private const string GraphEmailType = "graphEmail";

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("billingAccountId")]
    public string? BillingAccountId { get; init; }

    [JsonPropertyName("senderEmailAddress")]
    public string? SenderEmailAddress { get; init; }

    [JsonPropertyName("bodyPattern")]
    public string? BodyPattern { get; init; }

    public IntegrationConfiguration ToConfiguration() =>
        Type switch
        {
            MicrosoftBillingType => new MicrosoftBillingIntegrationConfiguration(
                BillingAccountId ?? throw new InvalidOperationException(
                    "Integration configuration of type 'microsoftBilling' is missing billingAccountId.")),
            GraphEmailType => new GraphEmailIntegrationConfiguration(
                SenderEmailAddress ?? throw new InvalidOperationException(
                    "Integration configuration of type 'graphEmail' is missing senderEmailAddress."),
                BodyPattern ?? throw new InvalidOperationException(
                    "Integration configuration of type 'graphEmail' is missing bodyPattern.")),
            _ => throw new InvalidOperationException($"Unrecognized integration configuration type '{Type}'."),
        };

    public static IntegrationConfigurationDocument FromConfiguration(IntegrationConfiguration configuration) =>
        configuration switch
        {
            MicrosoftBillingIntegrationConfiguration billing => new()
            {
                Type = MicrosoftBillingType,
                BillingAccountId = billing.BillingAccountId,
            },
            GraphEmailIntegrationConfiguration email => new()
            {
                Type = GraphEmailType,
                SenderEmailAddress = email.SenderEmailAddress,
                BodyPattern = email.BodyPattern,
            },
        };
}

internal sealed class OneDriveFolderDocument
{
    [JsonPropertyName("driveId")]
    public required string DriveId { get; init; }

    [JsonPropertyName("driveName")]
    public required string DriveName { get; init; }

    [JsonPropertyName("folderItemId")]
    public required string FolderItemId { get; init; }

    [JsonPropertyName("folderPath")]
    public required string FolderPath { get; init; }

    public OneDriveFolder ToFolder() => new(DriveId, DriveName, FolderItemId, FolderPath);

    public static OneDriveFolderDocument FromFolder(OneDriveFolder folder) => new()
    {
        DriveId = folder.DriveId,
        DriveName = folder.DriveName,
        FolderItemId = folder.FolderItemId,
        FolderPath = folder.FolderPath,
    };
}

internal sealed class InvoiceConfigurationRevisionDocument
{
    public const string RevisionDocumentType = "invoiceConfigurationRevision";

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("documentType")]
    public string DocumentType { get; init; } = RevisionDocumentType;

    [JsonPropertyName("partitionKey")]
    public string PartitionKey { get; init; } = InvoiceConfigurationDocument.ConfigurationPartitionKey;

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
