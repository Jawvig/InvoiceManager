using NodaMoney;

namespace InvoiceManager.Core.Integrations;

/// <summary>
/// The shared date/amount/currency tolerance rules used to decide whether a
/// candidate invoice matches. Both <see cref="InvoiceSearchCriteria"/> (source
/// matching) and <see cref="OneDriveSearchCriteria"/> (reconciliation) go through
/// here so the two matchers cannot drift apart.
/// </summary>
internal static class InvoiceMatching
{
    /// <summary>
    /// Whether a candidate's date is within <paramref name="dateToleranceDays"/> of
    /// the expected date, its currency equals the expected currency, and its amount
    /// is within <paramref name="amountTolerance"/> of the expected amount.
    /// </summary>
    public static bool DateAndOptionalAmountMatch(
        DateOnly expectedDate,
        int dateToleranceDays,
        Option<AmountMatchingCriteria> amountCriteria,
        DateOnly actualDate,
        Money actualAmount)
    {
        var dateMatches = DateDistanceDays(expectedDate, actualDate) <= dateToleranceDays;
        if (!dateMatches)
            return false;

        return amountCriteria switch
        {
            AmountMatchingCriteria criteria =>
                string.Equals(actualAmount.Currency.Code, criteria.Amount.Currency.Code, StringComparison.OrdinalIgnoreCase)
                && Math.Abs(actualAmount.Amount - criteria.Amount.Amount) <= criteria.AmountTolerance,
            None => true,
            _ => false,
        };
    }

    /// <summary>The absolute number of days between a candidate's date and the expected date.</summary>
    public static int DateDistanceDays(DateOnly expectedDate, DateOnly actualDate) =>
        Math.Abs(actualDate.DayNumber - expectedDate.DayNumber);
}
