using System.Globalization;
using NodaMoney;

namespace InvoiceManager.Core;

/// <summary>Settings used when generating invoice filenames.</summary>
public sealed record InvoiceFilenameSettings
{
    /// <summary>The culture used for amount formatting in filenames.</summary>
    public required CultureInfo Culture { get; init; }
}

/// <summary>
/// The parts read back from a canonical invoice filename. Mirrors the inputs to
/// <see cref="InvoiceFilename.Generate"/>.
/// </summary>
public sealed record ParsedInvoiceFilename(
    DateOnly InvoiceDate,
    string InvoiceDescription,
    string InvoiceName,
    Money Amount,
    VatMode VatMode);

/// <summary>
/// Generates the canonical OneDrive filename for a saved invoice.
/// </summary>
public sealed class InvoiceFilename
{
    private readonly InvoiceFilenameSettings settings;

    public InvoiceFilename(InvoiceFilenameSettings settings)
    {
        this.settings = settings;
    }

    public string Generate(
        DateOnly invoiceDate,
        string invoiceDescription,
        string invoiceName,
        Money amount,
        VatMode vatMode)
    {
        // The date is an ISO 8601 value and must stay calendar-invariant. Formatting
        // it with the configured culture would render the year in that culture's
        // default calendar (for example a Thai Buddhist or Hijri year), so the
        // invariant culture is used here regardless of the configured culture.
        var date = invoiceDate.ToString("O", CultureInfo.InvariantCulture);
        var money = FormatMoney(amount, settings.Culture);
        var vat = vatMode == VatMode.Inclusive ? "inc" : "exc";

        return $"{date} {invoiceDescription} {invoiceName} {money} {vat}.pdf";
    }

    /// <summary>
    /// The reverse of <see cref="Generate"/>: reads a canonical filename back into
    /// its parts so reconciliation can match a candidate file. Returns
    /// <see langword="false"/> (and a null <paramref name="result"/>) for any name
    /// that does not follow the convention — a malformed name is never an error and
    /// never partially matches.
    /// </summary>
    public bool TryParse(string fileName, out ParsedInvoiceFilename? result)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(fileName) ||
            !fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var stem = fileName[..^".pdf".Length];
        var tokens = stem.Split(' ');

        // A canonical name is "date description… sourceId money… vat": at least five
        // single-space-separated tokens with no empty segments (no double spaces).
        if (tokens.Length < 5 || Array.Exists(tokens, string.IsNullOrEmpty))
            return false;

        if (ParseVatMode(tokens[^1]) is not VatMode vatMode)
            return false;

        var moneyEnd = tokens.Length - 1; // exclusive of the vat token
        if (!TryTakeMoney(tokens, moneyEnd, out var amount, out var moneyStart))
            return false;

        // What remains before the money is "date description… sourceId": date, at
        // least one description token, and the source id.
        if (moneyStart < 3)
            return false;

        if (!DateOnly.TryParseExact(tokens[0], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return false;

        var invoiceName = tokens[moneyStart - 1];
        var description = string.Join(' ', tokens, 1, moneyStart - 2);

        result = new ParsedInvoiceFilename(date, description, invoiceName, amount, vatMode);
        return true;
    }

    private static VatMode? ParseVatMode(string token) => token switch
    {
        "inc" => VatMode.Inclusive,
        "exc" => VatMode.Exclusive,
        _ => null,
    };

    // Reads the money that Generate wrote: either "<symbol><amount>" for the big
    // three currencies, or "<symbol><amount> <ISO>" for every other. On success,
    // amount is set and moneyStart is the index of the first money token.
    private bool TryTakeMoney(string[] tokens, int end, out Money amount, out int moneyStart)
    {
        amount = default;
        moneyStart = end;

        var lastToken = tokens[end - 1];

        if (IsIsoCurrencyCode(lastToken))
        {
            // "<symbol><amount> <ISO>": the amount token is the one before the code.
            moneyStart = end - 2;
            return moneyStart >= 0 && TryParseMoney(tokens[moneyStart], lastToken, requireSymbol: true, out amount);
        }

        // "<symbol><amount>" for GBP/USD/EUR: the symbol determines the currency.
        moneyStart = end - 1;
        var symbolCurrency = lastToken.Length > 0
            ? lastToken[0] switch { '£' => "GBP", '$' => "USD", '€' => "EUR", _ => null }
            : null;

        return symbolCurrency is not null && TryParseMoney(lastToken, symbolCurrency, requireSymbol: true, out amount);
    }

    private bool TryParseMoney(string token, string currencyCode, bool requireSymbol, out Money amount)
    {
        amount = default;

        var firstDigit = token.AsSpan().IndexOfAnyInRange('0', '9');
        if (firstDigit < 0 || (requireSymbol && firstDigit == 0))
            return false;

        var number = token[firstDigit..];
        if (!decimal.TryParse(number, NumberStyles.Number, settings.Culture, out var value))
            return false;

        try
        {
            amount = new Money(value, currencyCode);
            return true;
        }
        catch (InvalidCurrencyException)
        {
            // NodaMoney rejects an unknown ISO code: treat as a non-matching name.
            return false;
        }
    }

    private static bool IsIsoCurrencyCode(string token)
    {
        if (token.Length != 3)
            return false;

        foreach (var c in token)
        {
            if (c is < 'A' or > 'Z')
                return false;
        }

        return true;
    }

    private static string FormatMoney(Money amount, CultureInfo culture)
    {
        var currency = amount.Currency;
        var formattedAmount = amount.Amount.ToString(culture);

        // The three currencies expected to comprise the vast majority of invoices
        // are shown with their symbol alone. Every other currency adds the ISO
        // 4217 code to disambiguate shared symbols (for example AUD/CAD using "$").
        return currency.Code switch
        {
            "GBP" => $"£{formattedAmount}",
            "USD" => $"${formattedAmount}",
            "EUR" => $"€{formattedAmount}",
            _ => $"{currency.Symbol}{formattedAmount} {currency.Code}",
        };
    }
}
