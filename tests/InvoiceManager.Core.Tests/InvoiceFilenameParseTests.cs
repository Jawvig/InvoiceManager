using System.Globalization;
using InvoiceManager.Core;
using NodaMoney;

namespace InvoiceManager.Core.Tests;

/// <summary>
/// Tests the reverse of <see cref="InvoiceFilename.Generate"/>: parsing a
/// canonical filename back into its parts so OneDrive reconciliation can read a
/// candidate file's date, amount, currency, and source id. Names that do not
/// follow the convention must be rejected (never throw, never partially match).
/// </summary>
public sealed class InvoiceFilenameParseTests
{
    private readonly InvoiceFilename invoiceFilename = new(
        new InvoiceFilenameSettings { Culture = CultureInfo.GetCultureInfo("en-GB") });

    [Theory]
    [InlineData("2026-07-10 Microsoft 365 Business Basic G152207778 £11.59 exc.pdf",
        "2026-07-10", "Microsoft 365 Business Basic", "G152207778", 11.59, "GBP", VatMode.Exclusive)]
    [InlineData("2026-07-10 ChatGPT Plus INV-001 $20.00 inc.pdf",
        "2026-07-10", "ChatGPT Plus", "INV-001", 20.00, "USD", VatMode.Inclusive)]
    [InlineData("2026-07-10 Some Service INV-002 €15.00 exc.pdf",
        "2026-07-10", "Some Service", "INV-002", 15.00, "EUR", VatMode.Exclusive)]
    [InlineData("2026-07-10 Some Service INV-003 $12.00 AUD exc.pdf",
        "2026-07-10", "Some Service", "INV-003", 12.00, "AUD", VatMode.Exclusive)]
    [InlineData("2026-07-10 Some Service INV-004 ¥1500 JPY exc.pdf",
        "2026-07-10", "Some Service", "INV-004", 1500, "JPY", VatMode.Exclusive)]
    public void TryParse_ReadsAllComponents_ForCanonicalNames(
        string fileName,
        string expectedDate,
        string expectedDescription,
        string expectedName,
        double expectedAmount,
        string expectedCurrency,
        VatMode expectedVatMode)
    {
        var parsed = invoiceFilename.TryParse(fileName, out var result);

        Assert.True(parsed);
        Assert.NotNull(result);
        Assert.Equal(DateOnly.ParseExact(expectedDate, "O", CultureInfo.InvariantCulture), result.InvoiceDate);
        Assert.Equal(expectedDescription, result.InvoiceDescription);
        Assert.Equal(expectedName, result.InvoiceName);
        Assert.Equal(new Money((decimal)expectedAmount, expectedCurrency), result.Amount);
        Assert.Equal(expectedVatMode, result.VatMode);
    }

    [Fact]
    public void TryParse_RoundTripsEveryGeneratedName()
    {
        var date = new DateOnly(2026, 7, 10);
        var amount = new Money(11.59m, "GBP");
        var generated = invoiceFilename.Generate(date, "Microsoft 365 Business Basic", "G152207778", amount, VatMode.Exclusive);

        Assert.True(invoiceFilename.TryParse(generated, out var result));
        Assert.Equal(date, result!.InvoiceDate);
        Assert.Equal("Microsoft 365 Business Basic", result.InvoiceDescription);
        Assert.Equal("G152207778", result.InvoiceName);
        Assert.Equal(amount, result.Amount);
        Assert.Equal(VatMode.Exclusive, result.VatMode);
    }

    [Theory]
    // Not a PDF / wrong extension.
    [InlineData("2026-07-10 Some Service INV-001 £11.59 exc")]
    [InlineData("2026-07-10 Some Service INV-001 £11.59 exc.txt")]
    // Malformed date.
    [InlineData("2026-7-10 Some Service INV-001 £11.59 exc.pdf")]
    [InlineData("10-07-2026 Some Service INV-001 £11.59 exc.pdf")]
    [InlineData("not-a-date Some Service INV-001 £11.59 exc.pdf")]
    // Missing or unknown VAT token.
    [InlineData("2026-07-10 Some Service INV-001 £11.59.pdf")]
    [InlineData("2026-07-10 Some Service INV-001 £11.59 incl.pdf")]
    [InlineData("2026-07-10 Some Service INV-001 £11.59 vat.pdf")]
    // Amount without a currency symbol.
    [InlineData("2026-07-10 Some Service INV-001 11.59 exc.pdf")]
    // Ambiguous symbol with no ISO code (never produced for a non-big-three currency).
    [InlineData("2026-07-10 Some Service INV-001 ¥1500 exc.pdf")]
    // Unknown ISO code.
    [InlineData("2026-07-10 Some Service INV-001 $1.00 XYZ exc.pdf")]
    // Too few segments (no room for date + description + source id).
    [InlineData("2026-07-10 £11.59 exc.pdf")]
    [InlineData("2026-07-10 exc.pdf")]
    // Unrelated files that happen to share the folder.
    [InlineData("report.pdf")]
    [InlineData("Statement June 2026.pdf")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_Rejects_NamesThatDoNotFollowTheConvention(string fileName)
    {
        var parsed = invoiceFilename.TryParse(fileName, out var result);

        Assert.False(parsed);
        Assert.Null(result);
    }
}
