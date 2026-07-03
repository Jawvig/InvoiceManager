namespace InvoiceManager.Core;

/// <summary>
/// Expected record generation completed for the configuration (a record was
/// created, or one already existed for the calculated period).
/// </summary>
public sealed record GenerationSucceeded(InvoiceConfigurationId ConfigurationId);

/// <summary>
/// Expected record generation failed for the configuration; other
/// configurations in the same run are unaffected.
/// </summary>
public sealed record GenerationFailed(InvoiceConfigurationId ConfigurationId, Exception Exception);

/// <summary>The outcome of expected record generation for a single configuration.</summary>
public union ExpectedRecordGenerationResult(GenerationSucceeded, GenerationFailed);
