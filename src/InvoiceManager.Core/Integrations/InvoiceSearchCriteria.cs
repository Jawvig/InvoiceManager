using NodaMoney;

namespace InvoiceManager.Core.Integrations;

/// <summary>
/// Provider-independent criteria used to locate a source invoice. Built from an
/// expected invoice record and its configuration, and translated by each source
/// integration into provider-specific search and matching behaviour.
/// </summary>
/// <param name="IntegrationConfiguration">The integration-specific settings (billing account, or sender/body pattern).</param>
/// <param name="ExpectedDate">The nominal date the invoice is expected on.</param>
/// <param name="DateToleranceDays">How many days either side of <paramref name="ExpectedDate"/> a candidate may fall.</param>
/// <param name="AmountMatchingCriteria">Optional expected amount, currency, and tolerance.</param>
public sealed record InvoiceSearchCriteria(
    IntegrationConfiguration IntegrationConfiguration,
    DateOnly ExpectedDate,
    int DateToleranceDays,
    Option<AmountMatchingCriteria> AmountMatchingCriteria)
{
    /// <summary>
    /// Whether a candidate with the given actual date and amount satisfies these
    /// criteria. Date is always required; currency and amount are required only
    /// when <see cref="AmountMatchingCriteria"/> is present.
    /// </summary>
    public bool Matches(DateOnly actualDate, Money actualAmount) =>
        InvoiceMatching.DateAndOptionalAmountMatch(
            ExpectedDate, DateToleranceDays, AmountMatchingCriteria, actualDate, actualAmount);

    /// <summary>
    /// The absolute number of days between a candidate's actual date and the
    /// expected date, used to prefer the closest candidate when several match.
    /// </summary>
    public int DateDistanceDays(DateOnly actualDate) =>
        InvoiceMatching.DateDistanceDays(ExpectedDate, actualDate);
}
