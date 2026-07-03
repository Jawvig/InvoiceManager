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

    [JsonPropertyName("defaultExpectedAmount")]
    public required decimal DefaultExpectedAmount { get; init; }

    [JsonPropertyName("defaultExpectedCurrency")]
    public required string DefaultExpectedCurrency { get; init; }

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

    // Optional for backward compatibility: configurations seeded before amount
    // tolerance existed deserialise to 0, meaning an exact amount match.
    [JsonPropertyName("amountTolerance")]
    public decimal AmountTolerance { get; init; }

    public InvoiceConfiguration ToConfiguration() =>
        new(
            new InvoiceConfigurationId(Id),
            Enum.Parse<Core.IntegrationType>(IntegrationType, ignoreCase: true),
            InvoiceDescription,
            Enum.Parse<InvoiceFrequency>(Frequency, ignoreCase: true),
            new Money(DefaultExpectedAmount, DefaultExpectedCurrency),
            Enum.Parse<VatMode>(DefaultVatMode, ignoreCase: true),
            IsActive,
            OneDriveDestination,
            DateOnly.ParseExact(StartDate, "O", CultureInfo.InvariantCulture),
            BillingAccountId,
            DateToleranceDays,
            AmountTolerance);

    public static InvoiceConfigurationDocument FromConfiguration(InvoiceConfiguration config) =>
        new()
        {
            Id = config.Id.Value,
            IntegrationType = config.IntegrationType.ToString(),
            InvoiceDescription = config.InvoiceDescription,
            Frequency = config.Frequency.ToString(),
            DefaultExpectedAmount = config.DefaultExpectedAmount.Amount,
            DefaultExpectedCurrency = config.DefaultExpectedAmount.Currency.Code,
            DefaultVatMode = config.DefaultVatMode.ToString(),
            IsActive = config.IsActive,
            OneDriveDestination = config.OneDriveDestination,
            StartDate = config.StartDate.ToString("O", CultureInfo.InvariantCulture),
            BillingAccountId = config.BillingAccountId,
            DateToleranceDays = config.DateToleranceDays,
            AmountTolerance = config.AmountTolerance,
        };
}
