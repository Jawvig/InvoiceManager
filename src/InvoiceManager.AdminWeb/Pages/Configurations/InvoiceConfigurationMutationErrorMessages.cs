using InvoiceManager.Core;

namespace InvoiceManager.AdminWeb.Pages.Configurations;

/// <summary>
/// Renders an <see cref="InvoiceConfigurationMutationResult"/> failure into the same
/// human-readable message across every page that calls a mutating
/// <see cref="InvoiceConfigurationService"/> method, so Create/Edit/Import/Activate-Deactivate/
/// Restore all report a given failure identically without duplicating the wording at each call
/// site. Formatting a result for display is a presentation concern, so it lives here rather than
/// in <see cref="InvoiceConfigurationService"/> itself.
/// </summary>
internal static class InvoiceConfigurationMutationErrorMessages
{
    public static Option<string> TryGetMessage(InvoiceConfigurationMutationResult result) => result switch
    {
        StoredInvoiceConfiguration => Option.None,
        InvoiceConfigurationValidationFailed(var errors) => string.Join(" ", errors),
        DuplicateInvoiceConfigurationId(var id) => $"Invoice configuration ID '{id}' already exists.",
        DuplicateInvoiceConfigurationSearchCriteria(var conflictingId) =>
            $"Invoice configuration '{conflictingId}' already has the same search criteria. " +
            "Two configurations that would match the same source files are not supported.",
        InvoiceConfigurationConflict =>
            "This configuration changed after the page was loaded. Reload and review the latest values before saving again.",
    };
}
