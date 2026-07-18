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
/// <param name="AmountMatchingCriteria">Optional expected amount, currency, and tolerance.</param>
/// <param name="SenderEmailAddress">
/// For <see cref="IntegrationType.Microsoft365Email"/> sources, the exact
/// sender address a candidate email must come from. Empty and unused for
/// other sources.
/// </param>
/// <param name="BodyPattern">
/// For <see cref="IntegrationType.Microsoft365Email"/> sources, a regular expression a
/// candidate email's plain-text body must match. Empty and unused for other
/// sources.
/// </param>
public sealed record InvoiceSearchCriteria(
    string BillingAccountId,
    DateOnly ExpectedDate,
    int DateToleranceDays,
    Option<AmountMatchingCriteria> AmountMatchingCriteria,
    string SenderEmailAddress = "",
    string BodyPattern = "")
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
