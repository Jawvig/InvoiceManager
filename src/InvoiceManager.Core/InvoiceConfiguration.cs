namespace InvoiceManager.Core;

/// <summary>
/// A configuration entry describing an invoice that the service expects to
/// retrieve on a recurring basis.
/// </summary>
public sealed record InvoiceConfiguration(DateOnly StartDate, InvoiceFrequency Frequency);
