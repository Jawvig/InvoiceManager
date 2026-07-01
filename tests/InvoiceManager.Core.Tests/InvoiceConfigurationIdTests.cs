namespace InvoiceManager.Core.Tests;

public sealed class InvoiceConfigurationIdTests
{
    [Fact]
    public void Constructor_ThrowsArgumentException_WhenValueIsNull()
    {
        Assert.Throws<ArgumentException>(() => new InvoiceConfigurationId(null!));
    }

    [Fact]
    public void Constructor_ThrowsArgumentException_WhenValueIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new InvoiceConfigurationId(""));
    }

    [Fact]
    public void Constructor_Succeeds_WhenValueIsNonEmpty()
    {
        var id = new InvoiceConfigurationId("m365-business-basic");

        Assert.Equal("m365-business-basic", id.Value);
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        var id = new InvoiceConfigurationId("m365-copilot");

        Assert.Equal("m365-copilot", id.ToString());
    }
}
