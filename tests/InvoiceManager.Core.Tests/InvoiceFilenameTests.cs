using System.Globalization;
using InvoiceManager.Core;
using NodaMoney;

namespace InvoiceManager.Core.Tests;

public sealed class InvoiceFilenameTests
{
    private readonly InvoiceFilename invoiceFilename;

    public InvoiceFilenameTests()
    {
        invoiceFilename = new InvoiceFilename(
            new InvoiceFilenameSettings { Culture = CultureInfo.GetCultureInfo("en-GB") });
    }

    [Fact]
    public void Generate_AssemblesAllComponents_ForGbpInvoice()
    {
        var filename = invoiceFilename.Generate(
            invoiceDate: new DateOnly(2026, 7, 10),
            invoiceDescription: "Microsoft 365 Business Basic",
            invoiceName: "G152207778",
            amount: new Money(11.59m, "GBP"),
            vatMode: VatMode.Exclusive);

        Assert.Equal(
            "2026-07-10 Microsoft 365 Business Basic G152207778 £11.59 exc.pdf",
            filename);
    }

    [Fact]
    public void Generate_UsesDollarSymbolWithoutIsoSuffix_ForUsd()
    {
        var filename = invoiceFilename.Generate(
            invoiceDate: new DateOnly(2026, 7, 10),
            invoiceDescription: "ChatGPT Plus",
            invoiceName: "INV-001",
            amount: new Money(20.00m, "USD"),
            vatMode: VatMode.Inclusive);

        Assert.Equal(
            "2026-07-10 ChatGPT Plus INV-001 $20.00 inc.pdf",
            filename);
    }

    [Fact]
    public void Generate_OmitsDescription_ForAzureStyleName()
    {
        var filename = invoiceFilename.Generate(
            new DateOnly(2026, 6, 9), "", "G157021982", new Money(40.62m, "GBP"), VatMode.Inclusive);

        Assert.Equal("2026-06-09 G157021982 £40.62 inc.pdf", filename);
    }

    [Fact]
    public void Generate_UsesEuroSymbolWithoutIsoSuffix_ForEur()
    {
        var filename = invoiceFilename.Generate(
            invoiceDate: new DateOnly(2026, 7, 10),
            invoiceDescription: "Some Service",
            invoiceName: "INV-002",
            amount: new Money(15.00m, "EUR"),
            vatMode: VatMode.Exclusive);

        Assert.Equal(
            "2026-07-10 Some Service INV-002 €15.00 exc.pdf",
            filename);
    }

    [Fact]
    public void Generate_AppendsIsoCode_ForNonBigThreeCurrencySharingASymbol()
    {
        var filename = invoiceFilename.Generate(
            invoiceDate: new DateOnly(2026, 7, 10),
            invoiceDescription: "Some Service",
            invoiceName: "INV-003",
            amount: new Money(12.00m, "AUD"),
            vatMode: VatMode.Exclusive);

        Assert.Equal(
            "2026-07-10 Some Service INV-003 $12.00 AUD exc.pdf",
            filename);
    }

    [Fact]
    public void Generate_UsesNativeSymbolAndIsoCode_ForCurrencyWithItsOwnSymbol()
    {
        var filename = invoiceFilename.Generate(
            invoiceDate: new DateOnly(2026, 7, 10),
            invoiceDescription: "Some Service",
            invoiceName: "INV-004",
            amount: new Money(1500m, "JPY"),
            vatMode: VatMode.Exclusive);

        Assert.Equal(
            "2026-07-10 Some Service INV-004 ¥1500 JPY exc.pdf",
            filename);
    }

    [Fact]
    public void Generate_UsesConfiguredCultureRatherThanAmbientCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

            var filename = invoiceFilename.Generate(
                invoiceDate: new DateOnly(2026, 7, 10),
                invoiceDescription: "Microsoft 365 Business Basic",
                invoiceName: "G152207778",
                amount: new Money(11.59m, "GBP"),
                vatMode: VatMode.Exclusive);

            Assert.Equal(
                "2026-07-10 Microsoft 365 Business Basic G152207778 £11.59 exc.pdf",
                filename);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void Generate_KeepsGregorianIsoDate_WhenConfiguredCultureUsesAnotherCalendar()
    {
        var thaiInvoiceFilename = new InvoiceFilename(
            new InvoiceFilenameSettings { Culture = CultureInfo.GetCultureInfo("th-TH") });

        var filename = thaiInvoiceFilename.Generate(
            invoiceDate: new DateOnly(2026, 7, 10),
            invoiceDescription: "Microsoft 365 Business Basic",
            invoiceName: "G152207778",
            amount: new Money(11.59m, "GBP"),
            vatMode: VatMode.Exclusive);

        Assert.StartsWith("2026-07-10 ", filename);
    }
}
