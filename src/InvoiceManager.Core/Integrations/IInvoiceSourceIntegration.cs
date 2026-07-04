namespace InvoiceManager.Core.Integrations;

/// <summary>
/// A provider-specific source of invoices. Implementations translate
/// provider-independent <see cref="InvoiceSearchCriteria"/> into calls against
/// their external system, own the decision about whether a candidate matches,
/// and return the invoice PDF plus its actual values when one does.
/// </summary>
public interface IInvoiceSourceIntegration
{
    /// <summary>The integration type this source handles, used to select it for a configuration.</summary>
    IntegrationType IntegrationType { get; }

    /// <summary>
    /// Searches the source system for an invoice matching <paramref name="criteria"/>,
    /// returning either <see cref="NoInvoiceMatch"/> or an accepted <see cref="InvoiceMatch"/>.
    /// </summary>
    Task<InvoiceSourceResult> FindInvoiceAsync(InvoiceSearchCriteria criteria, CancellationToken cancellationToken = default);
}
