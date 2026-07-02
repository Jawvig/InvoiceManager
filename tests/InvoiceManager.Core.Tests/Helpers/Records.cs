using NodaMoney;

namespace InvoiceManager.Core.Tests;

internal static class Records
{
    public static InvoiceRecord Build(
        InvoiceConfiguration? config = null,
        DateOnly? expectedDate = null,
        ProcessingStatus status = ProcessingStatus.Expected,
        DateOnly? actualDate = null)
    {
        var resolvedConfig = config ?? Configurations.Build();
        return new InvoiceRecord(
            resolvedConfig.Id,
            resolvedConfig.InvoiceDescription,
            expectedDate ?? resolvedConfig.StartDate,
            resolvedConfig.DateToleranceDays,
            resolvedConfig.DefaultExpectedAmount,
            resolvedConfig.DefaultVatMode,
            status,
            actualDate.HasValue ? actualDate.Value : Option.None);
    }
}
