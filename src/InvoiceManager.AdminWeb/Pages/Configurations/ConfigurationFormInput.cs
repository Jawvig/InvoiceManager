using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using System.Text.RegularExpressions;
using InvoiceManager.Core;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using NodaMoney;

namespace InvoiceManager.AdminWeb.Pages.Configurations;

public sealed class ConfigurationFormInput
{
    [Required, RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    public string Id { get; set; } = "";

    [Required]
    public IntegrationType IntegrationType { get; set; } = IntegrationType.MicrosoftBilling;

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

    public string BillingAccountId { get; set; } = "";

    [EmailAddress]
    public string SenderEmailAddress { get; set; } = "";

    public string BodyPattern { get; set; } = "";

    [Range(0, 365)]
    public int DateToleranceDays { get; set; } = 5;

    // Manual-entry OneDrive folder fields. There is no discovery/picker source for
    // these in this phase (see MicrosoftResourceDiscovery); an operator fills them
    // in by hand until the OneDrive File Picker v8 integration replaces this.
    [Required]
    public string DriveId { get; set; } = "";

    [Required]
    public string DriveName { get; set; } = "";

    [Required]
    public string FolderItemId { get; set; } = "";

    [Required]
    public string FolderPath { get; set; } = "";

    public string OriginalBillingAccountId { get; set; } = "";
    public string ETag { get; set; } = "";

    public InvoiceConfiguration Build(
        bool isActive,
        IReadOnlyList<BillingAccountChoice> billingAccounts,
        bool allowUnchangedMissingSelections)
    {
        IntegrationConfiguration integrationConfiguration;
        if (IntegrationType == IntegrationType.GraphEmail)
        {
            if (string.IsNullOrWhiteSpace(SenderEmailAddress))
                throw new ArgumentException("Sender email address is required for Microsoft 365 email invoices.");
            if (!MailAddress.TryCreate(SenderEmailAddress.Trim(), out _))
                throw new ArgumentException("Sender email address must be valid.");
            if (string.IsNullOrWhiteSpace(BodyPattern))
                throw new ArgumentException("Body pattern is required for Microsoft 365 email invoices.");
            try
            {
                _ = new Regex(BodyPattern, RegexOptions.None, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException)
            {
                throw new ArgumentException("Body pattern must be a valid regular expression.");
            }
            integrationConfiguration = new GraphEmailIntegrationConfiguration(SenderEmailAddress.Trim(), BodyPattern);
        }
        else
        {
            if (!billingAccounts.Any(x => x.Id == BillingAccountId) &&
                !(allowUnchangedMissingSelections && BillingAccountId == OriginalBillingAccountId))
            {
                throw new ArgumentException("Select a billing account returned by discovery.");
            }
            integrationConfiguration = new MicrosoftBillingIntegrationConfiguration(BillingAccountId);
        }

        var folder = new OneDriveFolder(DriveId, DriveName, FolderItemId, FolderPath);

        Option<AmountMatchingCriteria> amount = Option.None;
        if (HasExpectedAmount)
        {
            if (ExpectedAmount is null)
                throw new ArgumentException("Expected amount is required when amount matching is enabled.");
            try
            {
                amount = new AmountMatchingCriteria(new Money(ExpectedAmount.Value, Currency), AmountTolerance);
            }
            catch (InvalidCurrencyException)
            {
                throw new ArgumentException("Currency must be a recognized ISO 4217 currency code.");
            }
        }

        return new(
            new InvoiceConfigurationId(Id),
            integrationConfiguration,
            InvoiceDescription?.Trim() ?? "",
            Frequency,
            amount,
            DefaultVatMode,
            isActive,
            folder,
            StartDate,
            DateToleranceDays);
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
            DateToleranceDays = configuration.DateToleranceDays,
            DriveId = configuration.OneDriveFolder.DriveId,
            DriveName = configuration.OneDriveFolder.DriveName,
            FolderItemId = configuration.OneDriveFolder.FolderItemId,
            FolderPath = configuration.OneDriveFolder.FolderPath,
            ETag = stored.ETag,
        };
        switch (configuration.IntegrationConfiguration)
        {
            case MicrosoftBillingIntegrationConfiguration billing:
                input.BillingAccountId = billing.BillingAccountId;
                input.OriginalBillingAccountId = billing.BillingAccountId;
                break;
            case GraphEmailIntegrationConfiguration email:
                input.SenderEmailAddress = email.SenderEmailAddress;
                input.BodyPattern = email.BodyPattern;
                break;
        }
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
