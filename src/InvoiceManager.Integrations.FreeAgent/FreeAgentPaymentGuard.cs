namespace InvoiceManager.Integrations.FreeAgent;

internal enum GuessLookup
{
    NotFound,
    ExactlyOne,
    Ambiguous,
}

internal readonly record struct GuessLookupResult(GuessLookup Outcome, BankTransactionExplanationWire? Explanation);

/// <summary>
/// Locates a genuine unapproved Bill Payment "Guess" explanation for a bill:
/// queries every bank account's explanations and filters to ones whose
/// <c>paid_bill</c> matches the target bill and are <c>marked_for_review</c>.
/// Never picks among multiple matches - reports ambiguity instead.
/// </summary>
internal static class FreeAgentPaymentGuard
{
    public static async Task<GuessLookupResult> FindGuessForBillAsync(
        FreeAgentApiClient client,
        IReadOnlyList<string> bankAccountUrls,
        string billUrl,
        CancellationToken cancellationToken)
    {
        var matches = new List<BankTransactionExplanationWire>();

        foreach (var bankAccountUrl in bankAccountUrls)
        {
            var explanations = await client.GetExplanationsAsync(bankAccountUrl, cancellationToken);
            matches.AddRange(explanations.Where(e =>
                string.Equals(e.PaidBill, billUrl, StringComparison.Ordinal) && e.MarkedForReview));
        }

        return matches.Count switch
        {
            0 => new GuessLookupResult(GuessLookup.NotFound, null),
            1 => new GuessLookupResult(GuessLookup.ExactlyOne, matches[0]),
            _ => new GuessLookupResult(GuessLookup.Ambiguous, null),
        };
    }
}
