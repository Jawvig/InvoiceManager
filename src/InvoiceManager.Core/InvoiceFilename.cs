using System.Globalization;
using NodaMoney;

namespace InvoiceManager.Core;

/// <summary>Settings used when generating invoice filenames.</summary>
public sealed record InvoiceFilenameSettings
{
    public static InvoiceFilenameSettings Default { get; } = new();

    /// <summary>The culture used for date and amount formatting in filenames.</summary>
    public CultureInfo Culture { get; init; } = CultureInfo.GetCultureInfo("en-GB");
}

/// <summary>
/// Generates the canonical OneDrive filename for a saved invoice.
/// </summary>
public static class InvoiceFilename
{
    public static string Generate(
        DateOnly invoiceDate,
        string invoiceDescription,
        string invoiceName,
        Money amount,
        VatMode vatMode,
        InvoiceFilenameSettings? settings = null)
    {
        var filenameSettings = settings ?? InvoiceFilenameSettings.Default;
        // The date is an ISO 8601 value and must stay calendar-invariant. Formatting
        // it with the configured culture would render the year in that culture's
        // default calendar (for example a Thai Buddhist or Hijri year), so the
        // invariant culture is used here regardless of the configured culture.
        var date = invoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var money = FormatMoney(amount, filenameSettings.Culture);
        var vat = vatMode == VatMode.Inclusive ? "inc" : "exc";

        return $"{date} {invoiceDescription} {invoiceName} {money} {vat}.pdf";
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
