namespace InvoiceManager.Core;

/// <summary>
/// A record representing an invoice that should exist by a particular date,
/// tracking its progress through retrieval, reconciliation, save, and beyond.
/// </summary>
public sealed record InvoiceRecord(
    InvoiceConfigurationId ConfigurationId,
    string InvoiceDescription,
    DateOnly ExpectedDate,
    int DateToleranceDays,
    Option<AmountMatchingCriteria> AmountMatchingCriteria,
    VatMode ExpectedVatMode,
    InvoiceWorkflowState State,
    InvoiceProcessingSnapshot? ProcessingSnapshot = null)
{
    public InvoiceRecordId Id { get; } = InvoiceRecordId.NewId(ExpectedDate, ConfigurationId);
}
