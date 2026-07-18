using Microsoft.Extensions.Options;

namespace InvoiceManager.Infrastructure.DocumentIntelligence;

/// <summary>
/// Tunable settings for the Azure AI Document Intelligence-backed PDF field
/// extractor.
/// </summary>
public sealed class DocumentIntelligenceOptions
{
    public const string SectionName = "DocumentIntelligence";

    /// <summary>The resource endpoint, e.g. <c>https://{resource}.cognitiveservices.azure.com</c>.</summary>
    public Uri? Endpoint { get; set; }

    /// <summary>The Document Intelligence REST API version.</summary>
    public string ApiVersion { get; set; } = "2024-11-30";

    /// <summary>The prebuilt model used to analyze invoice documents.</summary>
    public string ModelId { get; set; } = "prebuilt-invoice";

    /// <summary>How long to wait between analyze-status polls when no <c>Retry-After</c> header is present.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>The overall time budget for polling document analysis to completion.</summary>
    public TimeSpan MaxPollDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The minimum field-level confidence required to accept an extracted
    /// InvoiceDate/InvoiceTotal value. Below this, extraction is treated as failed
    /// rather than risking a wrong filename/amount.
    /// </summary>
    public double MinimumFieldConfidence { get; set; } = 0.7;
}

public sealed class DocumentIntelligenceOptionsValidator : IValidateOptions<DocumentIntelligenceOptions>
{
    public ValidateOptionsResult Validate(string? name, DocumentIntelligenceOptions options) =>
        options.Endpoint is null
            ? ValidateOptionsResult.Fail("DocumentIntelligence:Endpoint is required.")
            : ValidateOptionsResult.Success;
}
