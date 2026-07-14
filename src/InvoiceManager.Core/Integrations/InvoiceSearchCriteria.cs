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
    decimal AmountTolerance)
{
    /// <summary>
    /// Whether a candidate with the given actual date and amount satisfies these
    /// criteria: its date must fall within <see cref="DateToleranceDays"/> of
    /// <see cref="ExpectedDate"/>, its currency must equal the expected currency,
    /// and its amount must be within <see cref="AmountTolerance"/> of the expected
    /// amount.
    /// </summary>
    public bool Matches(DateOnly actualDate, Money actualAmount) =>
        InvoiceMatching.DateAmountAndCurrencyMatch(
            ExpectedDate, DateToleranceDays, ExpectedAmount, AmountTolerance, actualDate, actualAmount);

    /// <summary>
    /// The absolute number of days between a candidate's actual date and the
    /// expected date, used to prefer the closest candidate when several match.
    /// </summary>
    public int DateDistanceDays(DateOnly actualDate) =>
        InvoiceMatching.DateDistanceDays(ExpectedDate, actualDate);
}
