using System.Text.Json.Serialization;
using InvoiceManager.Core;
using NodaMoney;

namespace InvoiceManager.Infrastructure.CosmosDb;

/// <summary>
/// The Cosmos DB document shape for an invoice record.
/// Maps between the Cosmos JSON structure and <see cref="InvoiceRecord"/>.
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

    [JsonPropertyName("actualInvoiceDate")]
    public string? ActualInvoiceDate { get; init; }

    public InvoiceRecord ToRecord() =>
        new(
            new InvoiceConfigurationId(ConfigurationId),
            InvoiceDescription,
            DateOnly.ParseExact(ExpectedDate, "yyyy-MM-dd"),
            DateToleranceDays,
            new Money(ExpectedAmount, ExpectedCurrency),
            Enum.Parse<VatMode>(ExpectedVatMode, ignoreCase: true),
            Enum.Parse<ProcessingStatus>(Status, ignoreCase: true),
            ActualInvoiceDate is not null
                ? DateOnly.ParseExact(ActualInvoiceDate, "yyyy-MM-dd")
                : Option.None);

    public static InvoiceRecordDocument FromRecord(InvoiceRecord record) =>
        new()
        {
            Id = record.Id.Value,
            ConfigurationId = record.ConfigurationId.Value,
            InvoiceDescription = record.InvoiceDescription,
            ExpectedDate = record.ExpectedDate.ToString("yyyy-MM-dd"),
            DateToleranceDays = record.DateToleranceDays,
            ExpectedAmount = record.ExpectedAmount.Amount,
            ExpectedCurrency = record.ExpectedAmount.Currency.Code,
            ExpectedVatMode = record.ExpectedVatMode.ToString(),
            Status = record.Status.ToString(),
            ActualInvoiceDate = record.ActualInvoiceDate switch
            {
                DateOnly date => date.ToString("yyyy-MM-dd"),
                None => null,
            },
        };
}
