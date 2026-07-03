using NodaMoney;

namespace InvoiceManager.Core.Integrations;

/// <summary>
/// Provider-independent criteria used to locate a source invoice. Built from an
/// expected invoice record and its configuration, and translated by each source
/// integration into provider-specific search and matching behaviour.
/// </summary>
/// <param name="BillingAccountId">The source account to search (for Microsoft 365, the Azure Billing account id).</param>
/// <param name="ExpectedDate">The nominal date the invoice is expected on.</param>
/// <param name="DateToleranceDays">How many days either side of <paramref name="ExpectedDate"/> a candidate may fall.</param>
/// <param name="ExpectedAmount">The expected total, including currency.</param>
/// <param name="AmountTolerance">The permitted absolute difference from <paramref name="ExpectedAmount"/> (0 means an exact match).</param>
public sealed record InvoiceSearchCriteria(
    string BillingAccountId,
    DateOnly ExpectedDate,
    int DateToleranceDays,
    Money ExpectedAmount,
    decimal AmountTolerance);
