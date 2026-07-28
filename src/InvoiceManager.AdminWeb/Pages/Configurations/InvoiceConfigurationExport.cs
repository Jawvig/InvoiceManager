using System.Text.Json;
using InvoiceManager.Core;

namespace InvoiceManager.AdminWeb.Pages.Configurations;

/// <summary>
/// The file format for exporting/importing an <see cref="InvoiceConfiguration"/> between
/// environments. Built from and consumed via the domain model / <see cref="ConfigurationFormInput"/>
/// only — never from the Cosmos document type — so Cosmos-internal fields (document type,
/// partition key, etag) and environment-local plumbing (IsActive, ETag) never appear in the file.
/// </summary>
public sealed record InvoiceConfigurationExport
{
    public required string Id { get; init; }
    public required string IntegrationType { get; init; }
    public string? BillingAccountId { get; init; }
    public string? SenderEmailAddress { get; init; }
    public string? BodyPattern { get; init; }
    public required string InvoiceDescription { get; init; }
    public required string Frequency { get; init; }
    public decimal? ExpectedAmount { get; init; }
    public string? Currency { get; init; }
    public decimal? AmountTolerance { get; init; }
    public required string DefaultVatMode { get; init; }
    public required OneDriveFolderExport OneDriveFolder { get; init; }
    public required DateOnly StartDate { get; init; }
    public required int DateToleranceDays { get; init; }

    public static InvoiceConfigurationExport FromConfiguration(InvoiceConfiguration configuration) => new()
    {
        Id = configuration.Id.Value,
        IntegrationType = configuration.IntegrationType.ToString(),
        BillingAccountId = configuration.IntegrationConfiguration is MicrosoftBillingIntegrationConfiguration billing
            ? billing.BillingAccountId
            : null,
        SenderEmailAddress = configuration.IntegrationConfiguration is GraphEmailIntegrationConfiguration email
            ? email.SenderEmailAddress
            : null,
        BodyPattern = configuration.IntegrationConfiguration is GraphEmailIntegrationConfiguration email2
            ? email2.BodyPattern
            : null,
        InvoiceDescription = configuration.InvoiceDescription,
        Frequency = configuration.Frequency.ToString(),
        ExpectedAmount = configuration.AmountMatchingCriteria is AmountMatchingCriteria amount ? amount.Amount.Amount : null,
        Currency = configuration.AmountMatchingCriteria is AmountMatchingCriteria amount2 ? amount2.Amount.Currency.Code : null,
        AmountTolerance = configuration.AmountMatchingCriteria is AmountMatchingCriteria amount3 ? amount3.AmountTolerance : null,
        DefaultVatMode = configuration.DefaultVatMode.ToString(),
        OneDriveFolder = OneDriveFolderExport.FromFolder(configuration.OneDriveFolder),
        StartDate = configuration.StartDate,
        DateToleranceDays = configuration.DateToleranceDays,
    };

    /// <summary>
    /// Pre-fills a fresh <see cref="ConfigurationFormInput"/> for review on the Create-style
    /// import form. Nothing here bypasses validation: the OneDrive folder is re-verified against
    /// Graph and the billing account against the live discovery list the same way a manually
    /// entered value would be, via the normal <see cref="ConfigurationFormInput.Build"/> /
    /// folder-resolution path, when the user saves.
    /// </summary>
    public ConfigurationFormInput ToFormInput() => new()
    {
        Id = Id,
        IntegrationType = Enum.TryParse<Core.IntegrationType>(IntegrationType, out var type) ? type : null,
        InvoiceDescription = InvoiceDescription,
        Frequency = Enum.TryParse<InvoiceFrequency>(Frequency, out var frequency) ? frequency : InvoiceFrequency.Monthly,
        HasExpectedAmount = ExpectedAmount is not null,
        ExpectedAmount = ExpectedAmount,
        Currency = Currency ?? "GBP",
        AmountTolerance = AmountTolerance ?? 0,
        DefaultVatMode = Enum.TryParse<VatMode>(DefaultVatMode, out var vatMode) ? vatMode : VatMode.Exclusive,
        StartDate = StartDate,
        DateToleranceDays = DateToleranceDays,
        BillingAccountId = BillingAccountId ?? "",
        SenderEmailAddress = SenderEmailAddress ?? "",
        BodyPattern = BodyPattern ?? "",
        DriveId = OneDriveFolder.DriveId,
        DriveName = OneDriveFolder.DriveName,
        FolderItemId = OneDriveFolder.FolderItemId,
        FolderPath = OneDriveFolder.FolderPath,
    };
}

public sealed record OneDriveFolderExport
{
    public required string DriveId { get; init; }
    public required string DriveName { get; init; }
    public required string FolderItemId { get; init; }
    public required string FolderPath { get; init; }

    public static OneDriveFolderExport FromFolder(OneDriveFolder folder) => new()
    {
        DriveId = folder.DriveId,
        DriveName = folder.DriveName,
        FolderItemId = folder.FolderItemId,
        FolderPath = folder.FolderPath,
    };
}

public static class InvoiceConfigurationExportJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}
