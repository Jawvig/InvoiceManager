using InvoiceManager.Core;
using InvoiceManager.Core.Integrations;

namespace InvoiceManager.TestSupport;

/// <summary>
/// A source integration test double that returns a preset result and records the
/// criteria it was asked to search with.
/// </summary>
public sealed class FakeInvoiceSourceIntegration : IInvoiceSourceIntegration
{
    private readonly InvoiceSourceResult result;

    public FakeInvoiceSourceIntegration(
        InvoiceSourceResult result,
        IntegrationType integrationType = IntegrationType.MicrosoftBilling)
    {
        this.result = result;
        IntegrationType = integrationType;
    }

    public IntegrationType IntegrationType { get; }

    public IReadOnlyList<InvoiceSearchCriteria> Requests => requests;

    private readonly List<InvoiceSearchCriteria> requests = [];

    public Task<InvoiceSourceResult> FindInvoiceAsync(
        InvoiceSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        requests.Add(criteria);
        return Task.FromResult(result);
    }
}
