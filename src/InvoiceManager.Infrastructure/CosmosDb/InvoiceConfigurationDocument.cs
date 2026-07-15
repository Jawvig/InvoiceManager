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
    [JsonPropertyName("id")]
    public required string Id { get; init; }

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
    public required string OneDriveDestination { get; init; }

    [JsonPropertyName("startDate")]
    public required string StartDate { get; init; }

    [JsonPropertyName("billingAccountId")]
    public required string BillingAccountId { get; init; }

    [JsonPropertyName("dateToleranceDays")]
    public required int DateToleranceDays { get; init; }

    public InvoiceConfiguration ToConfiguration() =>
        new(
            new InvoiceConfigurationId(Id),
            Enum.Parse<Core.IntegrationType>(IntegrationType, ignoreCase: true),
            InvoiceDescription,
            Enum.Parse<InvoiceFrequency>(Frequency, ignoreCase: true),
            ToAmountMatchingCriteria(),
            Enum.Parse<VatMode>(DefaultVatMode, ignoreCase: true),
            IsActive,
            OneDriveDestination,
            DateOnly.ParseExact(StartDate, "O", CultureInfo.InvariantCulture),
            BillingAccountId,
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
            InvoiceDescription = config.InvoiceDescription,
            Frequency = config.Frequency.ToString(),
            AmountMatchingCriteria = config.AmountMatchingCriteria switch
            {
                AmountMatchingCriteria criteria => AmountMatchingCriteriaDocument.FromCriteria(criteria),
                None => null,
            },
            DefaultVatMode = config.DefaultVatMode.ToString(),
            IsActive = config.IsActive,
            OneDriveDestination = config.OneDriveDestination,
            StartDate = config.StartDate.ToString("O", CultureInfo.InvariantCulture),
            BillingAccountId = config.BillingAccountId,
            DateToleranceDays = config.DateToleranceDays,
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
