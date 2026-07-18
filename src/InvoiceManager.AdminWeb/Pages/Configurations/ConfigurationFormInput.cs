using System.ComponentModel.DataAnnotations;
using InvoiceManager.Core;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using NodaMoney;

namespace InvoiceManager.AdminWeb.Pages.Configurations;

public sealed class ConfigurationFormInput
{
    [Required, RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    public string Id { get; set; } = "";

    [Required]
    public IntegrationType IntegrationType { get; set; } = IntegrationType.Microsoft365;

    public string InvoiceDescription { get; set; } = "";

    [Required]
    public InvoiceFrequency Frequency { get; set; } = InvoiceFrequency.Monthly;

    public bool HasExpectedAmount { get; set; }
    public decimal? ExpectedAmount { get; set; }
    public string Currency { get; set; } = "GBP";
    public decimal AmountTolerance { get; set; }

    [Required]
    public VatMode DefaultVatMode { get; set; } = VatMode.Exclusive;

    [Required, DataType(DataType.Date)]
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    [Required]
    public string BillingAccountId { get; set; } = "";

    [Range(0, 365)]
    public int DateToleranceDays { get; set; } = 5;

    [Required]
    public string FolderItemId { get; set; } = "";

    public string OriginalBillingAccountId { get; set; } = "";
    public string OriginalFolderItemId { get; set; } = "";
    public string OriginalDriveId { get; set; } = "";
    public string OriginalDisplayPath { get; set; } = "";
    public string ETag { get; set; } = "";

    public InvoiceConfiguration Build(
        bool isActive,
        IReadOnlyList<BillingAccountChoice> billingAccounts,
        IReadOnlyList<OneDriveFolderChoice> folders,
        bool allowUnchangedMissingSelections,
        InvoiceConfiguration? original = null)
    {
        if (!billingAccounts.Any(x => x.Id == BillingAccountId) &&
            !(allowUnchangedMissingSelections && BillingAccountId == OriginalBillingAccountId))
            throw new ArgumentException("Select a billing account returned by discovery.");

        OneDriveDestination destination;
        if (folders.FirstOrDefault(x => x.Destination.FolderItemId == FolderItemId) is { } selected)
        {
            destination = selected.Destination;
        }
        else if (allowUnchangedMissingSelections && FolderItemId == OriginalFolderItemId &&
                 !string.IsNullOrWhiteSpace(OriginalDisplayPath))
        {
            destination = new(OriginalDisplayPath, OriginalDriveId, OriginalFolderItemId);
        }
        else
        {
            throw new ArgumentException("Select an existing OneDrive folder returned by discovery.");
        }

        Option<AmountMatchingCriteria> amount = Option.None;
        if (HasExpectedAmount)
        {
            if (ExpectedAmount is null)
                throw new ArgumentException("Expected amount is required when amount matching is enabled.");
            amount = new AmountMatchingCriteria(new Money(ExpectedAmount.Value, Currency), AmountTolerance);
        }

        return new(
            new InvoiceConfigurationId(Id),
            IntegrationType,
            InvoiceDescription?.Trim() ?? "",
            Frequency,
            amount,
            DefaultVatMode,
            isActive,
            destination,
            StartDate,
            BillingAccountId,
            DateToleranceDays,
            original?.SenderEmailAddress ?? "",
            original?.BodyPattern ?? "");
    }

    public static ConfigurationFormInput From(StoredInvoiceConfiguration stored)
    {
        var configuration = stored.Configuration;
        var input = new ConfigurationFormInput
        {
            Id = configuration.Id.Value,
            IntegrationType = configuration.IntegrationType,
            InvoiceDescription = configuration.InvoiceDescription,
            Frequency = configuration.Frequency,
            DefaultVatMode = configuration.DefaultVatMode,
            StartDate = configuration.StartDate,
            BillingAccountId = configuration.BillingAccountId,
            DateToleranceDays = configuration.DateToleranceDays,
            FolderItemId = configuration.OneDriveDestination.FolderItemId ?? "",
            OriginalBillingAccountId = configuration.BillingAccountId,
            OriginalFolderItemId = configuration.OneDriveDestination.FolderItemId ?? "",
            OriginalDriveId = configuration.OneDriveDestination.DriveId ?? "",
            OriginalDisplayPath = configuration.OneDriveDestination.DisplayPath,
            ETag = stored.ETag,
        };
        if (configuration.AmountMatchingCriteria is AmountMatchingCriteria amount)
        {
            input.HasExpectedAmount = true;
            input.ExpectedAmount = amount.Amount.Amount;
            input.Currency = amount.Amount.Currency.Code;
            input.AmountTolerance = amount.AmountTolerance;
        }
        return input;
    }
}
