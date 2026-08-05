using System.Text.Json;
using System.Text.Json.Serialization;
using InvoiceManager.Core;
using InvoiceManager.Core.Integrations.FreeAgent;

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
    public AmountMatchingCriteriaExport? AmountMatchingCriteria { get; init; }
    public required string DefaultVatMode { get; init; }
    public required OneDriveFolderExport OneDriveFolder { get; init; }
    public required DateOnly StartDate { get; init; }
    public required int DateToleranceDays { get; init; }
    public FreeAgentBillMatchingExport? FreeAgentMatching { get; init; }

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
        AmountMatchingCriteria = configuration.AmountMatchingCriteria is AmountMatchingCriteria amount
            ? AmountMatchingCriteriaExport.FromCriteria(amount)
            : null,
        DefaultVatMode = configuration.DefaultVatMode.ToString(),
        OneDriveFolder = OneDriveFolderExport.FromFolder(configuration.OneDriveFolder),
        StartDate = configuration.StartDate,
        DateToleranceDays = configuration.DateToleranceDays,
        FreeAgentMatching = configuration.FreeAgentMatching is FreeAgentBillMatching freeAgentMatching
            ? FreeAgentBillMatchingExport.FromMatching(freeAgentMatching)
            : null,
    };

    /// <summary>
    /// Throws <see cref="JsonException"/> when the file doesn't represent a fully valid,
    /// currently-understood configuration, so the caller can report it the same way as a
    /// malformed/unparsable file rather than letting bad data through silently or crashing.
    /// Two gaps this closes that <c>required</c> and <c>Enum.TryParse</c> don't catch on their
    /// own: System.Text.Json's <c>required</c> check only verifies a property was *present* in
    /// the JSON, so an explicit <c>"oneDriveFolder": null</c> still satisfies it and leaves
    /// <see cref="OneDriveFolder"/> null; and <c>Enum.TryParse</c> happily accepts numeric
    /// strings with no corresponding named member (e.g. "999"), so a value must also pass
    /// <see cref="Enum.IsDefined{TEnum}(TEnum)"/> to be accepted.
    /// </summary>
    public void Validate()
    {
        if (OneDriveFolder is null)
            throw new JsonException("The file is missing its OneDrive folder.");
        if (!Enum.TryParse<Core.IntegrationType>(IntegrationType, out var integrationType) || !Enum.IsDefined(integrationType))
            throw new JsonException($"'{IntegrationType}' is not a recognized integration type.");
        if (!Enum.TryParse<InvoiceFrequency>(Frequency, out var frequency) || !Enum.IsDefined(frequency))
            throw new JsonException($"'{Frequency}' is not a recognized frequency.");
        if (!Enum.TryParse<VatMode>(DefaultVatMode, out var vatMode) || !Enum.IsDefined(vatMode))
            throw new JsonException($"'{DefaultVatMode}' is not a recognized VAT mode.");
    }

    /// <summary>
    /// Pre-fills a fresh <see cref="ConfigurationFormInput"/> for review on the Create-style
    /// import form. Nothing here bypasses validation: the OneDrive folder is re-verified against
    /// Graph and the billing account against the live discovery list the same way a manually
    /// entered value would be, via the normal <see cref="ConfigurationFormInput.Build"/> /
    /// folder-resolution path, when the user saves. Calls <see cref="Validate"/> first, so an
    /// invalid file is reported rather than reaching this dereference/parse.
    /// </summary>
    public ConfigurationFormInput ToFormInput()
    {
        Validate();
        return new()
        {
            Id = Id,
            IntegrationType = Enum.Parse<Core.IntegrationType>(IntegrationType),
            InvoiceDescription = InvoiceDescription,
            Frequency = Enum.Parse<InvoiceFrequency>(Frequency),
            HasExpectedAmount = AmountMatchingCriteria is not null,
            ExpectedAmount = AmountMatchingCriteria?.ExpectedAmount,
            Currency = AmountMatchingCriteria?.Currency ?? "GBP",
            AmountTolerance = AmountMatchingCriteria?.AmountTolerance ?? 0,
            DefaultVatMode = Enum.Parse<VatMode>(DefaultVatMode),
            StartDate = StartDate,
            DateToleranceDays = DateToleranceDays,
            BillingAccountId = BillingAccountId ?? "",
            SenderEmailAddress = SenderEmailAddress ?? "",
            BodyPattern = BodyPattern ?? "",
            DriveId = OneDriveFolder.DriveId,
            DriveName = OneDriveFolder.DriveName,
            FolderItemId = OneDriveFolder.FolderItemId,
            FolderPath = OneDriveFolder.FolderPath,
            HasFreeAgentMatching = FreeAgentMatching is not null,
            FreeAgentContactUrl = FreeAgentMatching?.ContactUrl ?? "",
            FreeAgentContactDisplayName = FreeAgentMatching?.ContactDisplayName ?? "",
            HasFreeAgentDateReconciliation = FreeAgentMatching?.DateToleranceDays is not null,
            FreeAgentDateToleranceDays = FreeAgentMatching?.DateToleranceDays ?? 0,
            HasFreeAgentAmountReconciliation = FreeAgentMatching?.AmountTolerance is not null,
            FreeAgentAmountTolerance = FreeAgentMatching?.AmountTolerance ?? 0,
        };
    }
}

public sealed record AmountMatchingCriteriaExport
{
    public required decimal ExpectedAmount { get; init; }
    public required string Currency { get; init; }
    public required decimal AmountTolerance { get; init; }

    public static AmountMatchingCriteriaExport FromCriteria(AmountMatchingCriteria criteria) => new()
    {
        ExpectedAmount = criteria.Amount.Amount,
        Currency = criteria.Amount.Currency.Code,
        AmountTolerance = criteria.AmountTolerance,
    };
}

public sealed record FreeAgentBillMatchingExport
{
    public required string ContactUrl { get; init; }
    public required string ContactDisplayName { get; init; }
    public int? DateToleranceDays { get; init; }
    public decimal? AmountTolerance { get; init; }

    public static FreeAgentBillMatchingExport FromMatching(FreeAgentBillMatching matching) => new()
    {
        ContactUrl = matching.Contact.Url.Url.OriginalString,
        ContactDisplayName = matching.Contact.DisplayName,
        DateToleranceDays = matching.DateReconciliation is FreeAgentDateReconciliation dateReconciliation
            ? dateReconciliation.ToleranceDays
            : null,
        AmountTolerance = matching.AmountReconciliation is FreeAgentAmountReconciliation amountReconciliation
            ? amountReconciliation.AmountTolerance
            : null,
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
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
