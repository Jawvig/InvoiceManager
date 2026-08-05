using System.Globalization;
using InvoiceManager.Core;
using InvoiceManager.Core.Integrations.FreeAgent;
using NodaMoney;

namespace InvoiceManager.Integrations.FreeAgent;

/// <summary>Shared date/money parsing for the FreeAgent wire-to-domain mapping extensions in this file.</summary>
file static class FreeAgentWireParsing
{
    public static DateOnly ParseDate(string? value, string fieldName) =>
        value is { Length: > 0 }
            ? DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : throw new InvalidOperationException($"FreeAgent bill is missing '{fieldName}'.");

    public static Money ParseMoney(string? value, string currency, string fieldName) =>
        value is { Length: > 0 }
            ? new Money(decimal.Parse(value, CultureInfo.InvariantCulture), currency)
            : throw new InvalidOperationException($"FreeAgent bill is missing '{fieldName}'.");
}

/// <summary>Translates a <see cref="BillWire"/> into the Core-owned, provider-agnostic <see cref="FreeAgentBillSnapshot"/>.</summary>
internal static class BillWireExtensions
{
    extension(BillWire wire)
    {
        public FreeAgentBillSnapshot ToSnapshot()
        {
            if (wire.Url is not { } url)
                throw new InvalidOperationException("FreeAgent bill is missing its url.");

            var currency = wire.Currency ?? throw new InvalidOperationException("FreeAgent bill is missing 'currency'.");

            return new FreeAgentBillSnapshot(
                new FreeAgentBillIdentity(url),
                ToStatus(wire.Status),
                FreeAgentWireParsing.ParseDate(wire.DatedOn, "dated_on"),
                FreeAgentWireParsing.ParseDate(wire.DueOn, "due_on"),
                FreeAgentWireParsing.ParseMoney(wire.TotalValue, currency, "total_value"),
                FreeAgentWireParsing.ParseMoney(wire.PaidValue, currency, "paid_value"),
                FreeAgentWireParsing.ParseMoney(wire.DueValue, currency, "due_value"),
                wire.PaidOn is { Length: > 0 } paidOn ? DateOnly.ParseExact(paidOn, "yyyy-MM-dd", CultureInfo.InvariantCulture) : Option.None,
                wire.Contact is { } contact
                    ? new FreeAgentContactIdentity(contact)
                    : throw new InvalidOperationException("FreeAgent bill is missing 'contact'."),
                wire.Reference ?? string.Empty,
                (wire.BillItems ?? []).Select(item => item.ToItem(currency)).ToList(),
                wire.Attachment is { } attachment ? attachment.ToAttachmentMetadata() : Option.None);
        }
    }

    private static FreeAgentBillStatus ToStatus(string? status) => status switch
    {
        "Open" => FreeAgentBillStatus.Open,
        "Paid" => FreeAgentBillStatus.Paid,
        "Part Paid" => FreeAgentBillStatus.PartPaid,
        "Overpaid" => FreeAgentBillStatus.Overpaid,
        _ => FreeAgentBillStatus.Unrecognised,
    };
}

/// <summary>Translates a <see cref="BillItemWire"/> into the Core-owned <see cref="FreeAgentBillItem"/>.</summary>
internal static class BillItemWireExtensions
{
    extension(BillItemWire wire)
    {
        public FreeAgentBillItem ToItem(string currency) =>
            new(
                new FreeAgentBillItemIdentity(wire.Url ?? throw new InvalidOperationException("FreeAgent bill item is missing its url.")),
                wire.Description ?? string.Empty,
                FreeAgentWireParsing.ParseMoney(wire.TotalValue, currency, "total_value"));
    }
}
