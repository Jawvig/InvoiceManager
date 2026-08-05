using System.Text.Json.Serialization;

namespace InvoiceManager.Integrations.FreeAgent;

internal sealed class BillItemWire
{
    public Uri? Url { get; init; }

    public Uri? Bill { get; init; }

    public string? Category { get; init; }

    public string? Description { get; init; }

    public string? TotalValue { get; init; }
}

internal sealed class AttachmentWire
{
    public Uri? Url { get; init; }

    public string? ContentType { get; init; }

    public string? FileName { get; init; }

    public long FileSize { get; init; }
}

internal sealed class BillWire
{
    public Uri? Url { get; init; }

    public Uri? Contact { get; init; }

    public string? Reference { get; init; }

    public string? DatedOn { get; init; }

    public string? DueOn { get; init; }

    public string? PaidOn { get; init; }

    public string? Currency { get; init; }

    public string? TotalValue { get; init; }

    public string? PaidValue { get; init; }

    public string? DueValue { get; init; }

    public string? Status { get; init; }

    public AttachmentWire? Attachment { get; init; }

    public IReadOnlyList<BillItemWire>? BillItems { get; init; }
}

internal sealed class BillsResponseWire
{
    public IReadOnlyList<BillWire> Bills { get; init; } = [];
}

internal sealed class BillResponseWire
{
    public BillWire? Bill { get; init; }
}

internal sealed class BankAccountWire
{
    public Uri? Url { get; init; }
}

internal sealed class BankAccountsResponseWire
{
    public IReadOnlyList<BankAccountWire> BankAccounts { get; init; } = [];
}

internal sealed class BankTransactionExplanationWire
{
    public Uri? Url { get; init; }

    public Uri? BankTransaction { get; init; }

    /// <summary>The bill this explanation pays, if any - a URI to the bill resource, not a bare identifier.</summary>
    public Uri? PaidBill { get; init; }

    public bool MarkedForReview { get; init; }

    public bool IsLocked { get; init; }

    public bool IsDeletable { get; init; }
}

internal sealed class BankTransactionExplanationsResponseWire
{
    // The wire field is "bank_transaction_explanations", not "explanations" - the naming
    // policy alone would produce the latter, so this one property still needs an explicit
    // JsonPropertyName even though every other property in this file doesn't.
    [JsonPropertyName("bank_transaction_explanations")]
    public IReadOnlyList<BankTransactionExplanationWire> Explanations { get; init; } = [];
}

internal sealed class BankTransactionWire
{
    public Uri? Url { get; init; }

    public string? UnexplainedAmount { get; init; }

    [JsonPropertyName("bank_transaction_explanations")]
    public IReadOnlyList<Uri>? Explanations { get; init; }
}

internal sealed class BankTransactionResponseWire
{
    public BankTransactionWire? BankTransaction { get; init; }
}

/// <summary>The shape of a FreeAgent 422 validation-error body, used to detect the "locked" signal proven by the POC.</summary>
internal sealed class ErrorResponseWire
{
    public ErrorsWire? Errors { get; init; }
}

internal sealed class ErrorsWire
{
    // FreeAgent's error shape varies (an object keyed by field, or an array); captured as raw
    // text and searched for the known locked-field substrings rather than modelled precisely.
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? Fields { get; init; }
}
