using NodaMoney;

namespace InvoiceManager.Core;

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
        VatMode vatMode)
    {
        var date = invoiceDate.ToString("yyyy-MM-dd");
        var money = FormatMoney(amount);
        var vat = vatMode == VatMode.Inclusive ? "inc" : "exc";

        return $"{date} {invoiceDescription} {invoiceName} {money} {vat}.pdf";
    }

    private static string FormatMoney(Money amount)
    {
        var currency = amount.Currency;

        // The three currencies expected to comprise the vast majority of invoices
        // are shown with their symbol alone. Every other currency adds the ISO
        // 4217 code to disambiguate shared symbols (for example AUD/CAD using "$").
        return currency.Code switch
        {
            "GBP" => $"£{amount.Amount}",
            "USD" => $"${amount.Amount}",
            "EUR" => $"€{amount.Amount}",
            _ => $"{currency.Symbol}{amount.Amount} {currency.Code}",
        };
    }
}
