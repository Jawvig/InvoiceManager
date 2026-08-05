using System.Globalization;
using InvoiceManager.Core;
using InvoiceManager.Core.Integrations.FreeAgent;
using NodaMoney;

namespace InvoiceManager.Integrations.FreeAgent;

/// <summary>Translates FreeAgent wire DTOs into Core-owned, provider-agnostic types. Never lets a wire type escape this project.</summary>
internal static class FreeAgentBillMapping
{
    public static FreeAgentBillSnapshot ToSnapshot(BillWire wire)
    {
        if (wire.Url is not { } url)
            throw new InvalidOperationException("FreeAgent bill is missing its url.");

        var currency = wire.Currency ?? throw new InvalidOperationException("FreeAgent bill is missing 'currency'.");

        return new FreeAgentBillSnapshot(
            new FreeAgentBillIdentity(url),
            ToStatus(wire.Status),
            ParseDate(wire.DatedOn, "dated_on"),
            ParseDate(wire.DueOn, "due_on"),
            ParseMoney(wire.TotalValue, currency, "total_value"),
            ParseMoney(wire.PaidValue, currency, "paid_value"),
            ParseMoney(wire.DueValue, currency, "due_value"),
            wire.PaidOn is { Length: > 0 } paidOn ? DateOnly.ParseExact(paidOn, "yyyy-MM-dd", CultureInfo.InvariantCulture) : Option.None,
            wire.Contact is { Length: > 0 } contact
                ? new FreeAgentContactIdentity(contact)
                : throw new InvalidOperationException("FreeAgent bill is missing 'contact'."),
            wire.Reference ?? string.Empty,
            (wire.BillItems ?? []).Select(item => ToItem(item, currency)).ToList(),
            wire.Attachment is { } attachment ? ToAttachmentMetadata(attachment) : Option.None);
    }

    public static FreeAgentBillItem ToItem(BillItemWire wire, string currency) =>
        new(
            new FreeAgentBillItemIdentity(wire.Url ?? throw new InvalidOperationException("FreeAgent bill item is missing its url.")),
            wire.Description ?? string.Empty,
            ParseMoney(wire.TotalValue, currency, "total_value"));

    public static FreeAgentAttachmentMetadata ToAttachmentMetadata(AttachmentWire wire) =>
        new(
            wire.FileName ?? string.Empty,
            wire.FileSize,
            wire.ContentType ?? "application/octet-stream",
            DateTimeOffset.UtcNow);

    private static FreeAgentBillStatus ToStatus(string? status) => status switch
    {
        "Open" => FreeAgentBillStatus.Open,
        "Paid" => FreeAgentBillStatus.Paid,
        "Part Paid" => FreeAgentBillStatus.PartPaid,
        "Overpaid" => FreeAgentBillStatus.Overpaid,
        _ => FreeAgentBillStatus.Unrecognised,
    };

    private static DateOnly ParseDate(string? value, string fieldName) =>
        value is { Length: > 0 }
            ? DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : throw new InvalidOperationException($"FreeAgent bill is missing '{fieldName}'.");

    private static Money ParseMoney(string? value, string currency, string fieldName) =>
        value is { Length: > 0 }
            ? new Money(decimal.Parse(value, CultureInfo.InvariantCulture), currency)
            : throw new InvalidOperationException($"FreeAgent bill is missing '{fieldName}'.");
}
