using System.Text.Json.Serialization;

namespace InvoiceManager.Integrations.FreeAgent;

internal sealed class BillItemWire
{
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("bill")]
    public string? Bill { get; init; }

    [JsonPropertyName("category")]
    public string? Category { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("total_value")]
    public string? TotalValue { get; init; }
}

internal sealed class AttachmentWire
{
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("content_type")]
    public string? ContentType { get; init; }

    [JsonPropertyName("file_name")]
    public string? FileName { get; init; }

    [JsonPropertyName("file_size")]
    public long FileSize { get; init; }
}

internal sealed class BillWire
{
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("contact")]
    public string? Contact { get; init; }

    [JsonPropertyName("reference")]
    public string? Reference { get; init; }

    [JsonPropertyName("dated_on")]
    public string? DatedOn { get; init; }

    [JsonPropertyName("due_on")]
    public string? DueOn { get; init; }

    [JsonPropertyName("paid_on")]
    public string? PaidOn { get; init; }

    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    [JsonPropertyName("total_value")]
    public string? TotalValue { get; init; }

    [JsonPropertyName("paid_value")]
    public string? PaidValue { get; init; }

    [JsonPropertyName("due_value")]
    public string? DueValue { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("attachment")]
    public AttachmentWire? Attachment { get; init; }

    [JsonPropertyName("bill_items")]
    public List<BillItemWire>? BillItems { get; init; }
}

internal sealed class BillsResponseWire
{
    [JsonPropertyName("bills")]
    public List<BillWire> Bills { get; init; } = [];
}

internal sealed class BillResponseWire
{
    [JsonPropertyName("bill")]
    public BillWire? Bill { get; init; }
}

internal sealed class BankAccountWire
{
    [JsonPropertyName("url")]
    public string? Url { get; init; }
}

internal sealed class BankAccountsResponseWire
{
    [JsonPropertyName("bank_accounts")]
    public List<BankAccountWire> BankAccounts { get; init; } = [];
}

internal sealed class BankTransactionExplanationWire
{
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("bank_transaction")]
    public string? BankTransaction { get; init; }

    [JsonPropertyName("paid_bill")]
    public string? PaidBill { get; init; }

    [JsonPropertyName("marked_for_review")]
    public bool MarkedForReview { get; init; }

    [JsonPropertyName("is_locked")]
    public bool IsLocked { get; init; }

    [JsonPropertyName("is_deletable")]
    public bool IsDeletable { get; init; }
}

internal sealed class BankTransactionExplanationsResponseWire
{
    [JsonPropertyName("bank_transaction_explanations")]
    public List<BankTransactionExplanationWire> Explanations { get; init; } = [];
}

internal sealed class BankTransactionWire
{
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("unexplained_amount")]
    public string? UnexplainedAmount { get; init; }

    [JsonPropertyName("bank_transaction_explanations")]
    public List<string>? Explanations { get; init; }
}

internal sealed class BankTransactionResponseWire
{
    [JsonPropertyName("bank_transaction")]
    public BankTransactionWire? BankTransaction { get; init; }
}

/// <summary>The shape of a FreeAgent 422 validation-error body, used to detect the "locked" signal proven by the POC.</summary>
internal sealed class ErrorResponseWire
{
    [JsonPropertyName("errors")]
    public ErrorsWire? Errors { get; init; }
}

internal sealed class ErrorsWire
{
    // FreeAgent's error shape varies (an object keyed by field, or an array); captured as raw
    // text and searched for the known locked-field substrings rather than modelled precisely.
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? Fields { get; init; }
}
