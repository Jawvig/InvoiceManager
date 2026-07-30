namespace InvoiceManager.Core;

/// <summary>
/// The submitted configuration failed validation - see
/// <see cref="InvoiceConfigurationValidation.Validate"/>.
/// </summary>
public sealed record InvoiceConfigurationValidationFailed(IReadOnlyList<string> Errors);

/// <summary>The configuration ID already belongs to a different configuration.</summary>
public sealed record DuplicateInvoiceConfigurationId(InvoiceConfigurationId Id);

/// <summary>
/// Another configuration (<see cref="ConflictingId"/>) already matches the same search
/// criteria - see <see cref="InvoiceConfigurationValidation.ValidateNoDuplicateMatch"/>.
/// </summary>
public sealed record DuplicateInvoiceConfigurationSearchCriteria(InvoiceConfigurationId ConflictingId);

/// <summary>
/// The configuration changed since it was loaded (an ETag mismatch on write). The caller should
/// reload the latest version and re-apply its edit against that.
/// </summary>
public sealed record InvoiceConfigurationConflict;

/// <summary>
/// The outcome of a mutating <see cref="InvoiceConfigurationService"/> call: either the resulting
/// stored configuration, or the specific reason it did not succeed. Every case here is a normal,
/// user-triggerable outcome of an otherwise well-formed request - callers are expected to switch
/// exhaustively over this rather than catching exceptions, so the compiler forces every case to be
/// handled explicitly instead of a forgotten catch silently falling through to a generic error
/// page. Failures that indicate a caller/API-contract violation instead of a legitimate business
/// outcome (e.g. attempting to change an immutable field) still throw <see cref="ArgumentException"/>,
/// since those are not reachable through the normal UI and should surface as bugs, not results.
/// </summary>
public union InvoiceConfigurationMutationResult(
    StoredInvoiceConfiguration,
    InvoiceConfigurationValidationFailed,
    DuplicateInvoiceConfigurationId,
    DuplicateInvoiceConfigurationSearchCriteria,
    InvoiceConfigurationConflict);
