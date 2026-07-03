namespace InvoiceManager.Core.Tests;

internal static class Records
{
    public static InvoiceRecord Build(
        InvoiceConfiguration? config = null,
        DateOnly? expectedDate = null,
        InvoiceWorkflowState? state = null)
    {
        var resolvedConfig = config ?? Configurations.Build();
        return new InvoiceRecord(
            resolvedConfig.Id,
            resolvedConfig.InvoiceDescription,
            expectedDate ?? resolvedConfig.StartDate,
            resolvedConfig.DateToleranceDays,
            resolvedConfig.DefaultExpectedAmount,
            resolvedConfig.DefaultVatMode,
            state ?? new Expected());
    }
}
