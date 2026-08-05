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

    [JsonPropertyName("freeAgentMatching")]
    public FreeAgentBillMatchingDocument? FreeAgentMatching { get; init; }

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
            DateToleranceDays,
            ToFreeAgentMatching());

    private Option<AmountMatchingCriteria> ToAmountMatchingCriteria() =>
        AmountMatchingCriteria is { } criteria
            ? criteria.ToCriteria()
            : Option.None;

    private Option<FreeAgentBillMatching> ToFreeAgentMatching() =>
        FreeAgentMatching is { } matching
            ? matching.ToMatching()
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
            FreeAgentMatching = config.FreeAgentMatching switch
            {
                FreeAgentBillMatching matching => FreeAgentBillMatchingDocument.FromMatching(matching),
                None => null,
            },
        };
}

/// <summary>The Cosmos JSON shape for <see cref="FreeAgentBillMatching"/>.</summary>
internal sealed class FreeAgentBillMatchingDocument
{
    [JsonPropertyName("contactUrl")]
    public required string ContactUrl { get; init; }

    [JsonPropertyName("dateReconciliation")]
    public FreeAgentDateReconciliationDocument? DateReconciliation { get; init; }

    [JsonPropertyName("amountReconciliation")]
    public FreeAgentAmountReconciliationDocument? AmountReconciliation { get; init; }

    public FreeAgentBillMatching ToMatching() =>
        new(
            ContactUrl,
            DateReconciliation is { } dateReconciliation ? dateReconciliation.ToReconciliation() : Option.None,
            AmountReconciliation is { } amountReconciliation ? amountReconciliation.ToReconciliation() : Option.None);

    public static FreeAgentBillMatchingDocument FromMatching(FreeAgentBillMatching matching) => new()
    {
        ContactUrl = matching.ContactUrl,
        DateReconciliation = matching.DateReconciliation switch
        {
            FreeAgentDateReconciliation dateReconciliation => FreeAgentDateReconciliationDocument.FromReconciliation(dateReconciliation),
            None => null,
        },
        AmountReconciliation = matching.AmountReconciliation switch
        {
            FreeAgentAmountReconciliation amountReconciliation => FreeAgentAmountReconciliationDocument.FromReconciliation(amountReconciliation),
            None => null,
        },
    };
}

/// <summary>The Cosmos JSON shape for <see cref="FreeAgentDateReconciliation"/>.</summary>
internal sealed class FreeAgentDateReconciliationDocument
{
    [JsonPropertyName("toleranceDays")]
    public required int ToleranceDays { get; init; }

    public FreeAgentDateReconciliation ToReconciliation() => new(ToleranceDays);

    public static FreeAgentDateReconciliationDocument FromReconciliation(FreeAgentDateReconciliation reconciliation) => new()
    {
        ToleranceDays = reconciliation.ToleranceDays,
    };
}

/// <summary>The Cosmos JSON shape for <see cref="FreeAgentAmountReconciliation"/>.</summary>
internal sealed class FreeAgentAmountReconciliationDocument
{
    [JsonPropertyName("amountTolerance")]
    public required decimal AmountTolerance { get; init; }

    public FreeAgentAmountReconciliation ToReconciliation() => new(AmountTolerance);

    public static FreeAgentAmountReconciliationDocument FromReconciliation(FreeAgentAmountReconciliation reconciliation) => new()
    {
        AmountTolerance = reconciliation.AmountTolerance,
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

/// <summary>
/// The sole ETag-protected sentinel document guarding the cross-configuration
/// duplicate-search-criteria check (<see cref="InvoiceConfigurationValidation.ValidateNoDuplicateMatch"/>).
/// It carries no meaningful content of its own - every write to it is a conditional replace with
/// the same body, whose only purpose is to force Cosmos to hand out a new ETag so a concurrent
/// writer that read the sentinel first loses the transactional batch with a 412 instead of both
/// writers' independently-passed validation silently standing. See docs/data-model.md's
/// "Duplicate-validation sentinel" section.
///
/// <para>
/// <see cref="SentinelId"/> deliberately contains underscores, which
/// <see cref="InvoiceConfigurationValidation.Validate"/>'s lowercase-kebab-case
/// <c>IdPattern</c> never allows in a real configuration ID - so this document's ID can never
/// collide with one a user creates, structurally, rather than by relying on a reserved-word check
/// in validation that a new call site could forget. Do not rename it to something that could pass
/// that pattern.
/// </para>
/// </summary>
internal sealed class ValidationSentinelDocument
{
    public const string SentinelId = "__duplicate_validation_sentinel__";
    public const string SentinelDocumentType = "invoiceConfigurationValidationSentinel";

    [JsonPropertyName("id")]
    public string Id { get; init; } = SentinelId;

    [JsonPropertyName("documentType")]
    public string DocumentType { get; init; } = SentinelDocumentType;

    [JsonPropertyName("partitionKey")]
    public string PartitionKey { get; init; } = InvoiceConfigurationDocument.ConfigurationPartitionKey;
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
