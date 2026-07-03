using InvoiceManager.Core;
using NodaMoney;

namespace InvoiceManager.TestSupport;

public static class Actuals
{
    public static ActualInvoiceDetails Build(
        DateOnly? actualInvoiceDate = null,
        Money? actualAmount = null,
        SourceInvoiceId? sourceInvoiceId = null) =>
        new(
            actualInvoiceDate ?? new DateOnly(2025, 1, 10),
            actualAmount ?? new Money(10.00m, "GBP"),
            sourceInvoiceId ?? new SourceInvoiceId("SRC-INVOICE-1"));
}
