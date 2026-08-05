using InvoiceManager.Core.Integrations.FreeAgent;

namespace InvoiceManager.Core.Tests;

public sealed class FreeAgentBillSnapshotTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-uri")]
    [InlineData("/v2/bills/1")] // relative, not absolute
    public void FreeAgentBillIdentity_Throws_ForNonAbsoluteUri(string billUrl) =>
        Assert.Throws<ArgumentException>(() => new FreeAgentBillIdentity(billUrl));

    [Theory]
    [InlineData("")]
    [InlineData("not-a-uri")]
    [InlineData("/v2/bill_items/1")]
    public void FreeAgentBillItemIdentity_Throws_ForNonAbsoluteUri(string itemUrl) =>
        Assert.Throws<ArgumentException>(() => new FreeAgentBillItemIdentity(itemUrl));

    [Theory]
    [InlineData("")]
    [InlineData("not-a-uri")]
    [InlineData("/v2/contacts/1")]
    public void FreeAgentContactIdentity_Throws_ForNonAbsoluteUri(string contactUrl) =>
        Assert.Throws<ArgumentException>(() => new FreeAgentContactIdentity(contactUrl));

    [Fact]
    public void FreeAgentBillIdentity_Accepts_AbsoluteUri() =>
        Assert.Equal(
            "https://api.sandbox.freeagent.com/v2/bills/1",
            new FreeAgentBillIdentity("https://api.sandbox.freeagent.com/v2/bills/1").BillUrl);
}
