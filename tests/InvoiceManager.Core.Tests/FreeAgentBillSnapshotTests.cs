using InvoiceManager.Core.Integrations.FreeAgent;

namespace InvoiceManager.Core.Tests;

public sealed class FreeAgentBillSnapshotTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-uri")]
    [InlineData("/v2/bills/1")] // relative path - on Unix, Uri.TryCreate parses this as an absolute file:// URI
    [InlineData("http://api.sandbox.freeagent.com/v2/bills/1")] // absolute but not https
    [InlineData("file:///v2/bills/1")]
    [InlineData("mailto:someone@example.com")]
    public void FreeAgentBillIdentity_Throws_ForNonHttpsAbsoluteUri(string billUrl) =>
        Assert.Throws<ArgumentException>(() => new FreeAgentBillIdentity(billUrl));

    [Theory]
    [InlineData("")]
    [InlineData("not-a-uri")]
    [InlineData("/v2/bill_items/1")]
    [InlineData("http://api.sandbox.freeagent.com/v2/bill_items/1")]
    public void FreeAgentBillItemIdentity_Throws_ForNonHttpsAbsoluteUri(string itemUrl) =>
        Assert.Throws<ArgumentException>(() => new FreeAgentBillItemIdentity(itemUrl));

    [Theory]
    [InlineData("")]
    [InlineData("not-a-uri")]
    [InlineData("/v2/contacts/1")]
    [InlineData("http://api.sandbox.freeagent.com/v2/contacts/1")]
    public void FreeAgentContactIdentity_Throws_ForNonHttpsAbsoluteUri(string contactUrl) =>
        Assert.Throws<ArgumentException>(() => new FreeAgentContactIdentity(contactUrl));

    [Fact]
    public void FreeAgentBillIdentity_Accepts_AbsoluteHttpsUri() =>
        Assert.Equal(
            "https://api.sandbox.freeagent.com/v2/bills/1",
            new FreeAgentBillIdentity("https://api.sandbox.freeagent.com/v2/bills/1").BillUrl);
}
