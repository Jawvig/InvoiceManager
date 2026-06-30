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
        var date = invoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var money = FormatMoney(amount, settings.Culture);
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
