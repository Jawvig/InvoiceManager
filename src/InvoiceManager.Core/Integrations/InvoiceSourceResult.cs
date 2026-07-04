namespace InvoiceManager.Core.Integrations;

/// <summary>
/// No source-system invoice satisfied the supplied criteria.
/// </summary>
public sealed record NoInvoiceMatch;

/// <summary>
/// A source-system invoice satisfied the criteria. Carries the downloaded PDF
/// bytes (the integration extracts a single PDF from any ZIP itself) together
/// with the actual values read from the invoice.
/// </summary>
public sealed record InvoiceMatch(byte[] PdfContent, ActualInvoiceDetails Details);

/// <summary>The outcome of asking a source integration to find an invoice.</summary>
public union InvoiceSourceResult(NoInvoiceMatch, InvoiceMatch);
