namespace InvoiceManager.Core;

/// <summary>
/// A configuration entry describing an invoice that the service expects to
/// retrieve on a recurring basis.
/// </summary>
public sealed record InvoiceConfiguration
{
    /// <summary>The date of the first expected invoice for this configuration.</summary>
    public required DateOnly StartDate { get; init; }

    /// <summary>The recurrence pattern used to derive later expected invoices.</summary>
    public required InvoiceFrequency Frequency { get; init; }
}
