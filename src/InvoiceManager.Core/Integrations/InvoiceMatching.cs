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
    public static bool DateAmountAndCurrencyMatch(
        DateOnly expectedDate,
        int dateToleranceDays,
        Money expectedAmount,
        decimal amountTolerance,
        DateOnly actualDate,
        Money actualAmount)
    {
        var dateMatches = DateDistanceDays(expectedDate, actualDate) <= dateToleranceDays;
        var currencyMatches = string.Equals(
            actualAmount.Currency.Code, expectedAmount.Currency.Code, StringComparison.OrdinalIgnoreCase);
        var amountMatches = Math.Abs(actualAmount.Amount - expectedAmount.Amount) <= amountTolerance;
        return dateMatches && currencyMatches && amountMatches;
    }

    /// <summary>The absolute number of days between a candidate's date and the expected date.</summary>
    public static int DateDistanceDays(DateOnly expectedDate, DateOnly actualDate) =>
        Math.Abs(actualDate.DayNumber - expectedDate.DayNumber);
}
