using System.ComponentModel;
using System.Text.Json;

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

    [Fact]
    public void Json_RoundTripsAsPlainString()
    {
        var id = new InvoiceConfigurationId("m365-copilot");

        var json = JsonSerializer.Serialize(id);
        Assert.Equal("\"m365-copilot\"", json);

        var deserialized = JsonSerializer.Deserialize<InvoiceConfigurationId>(json);
        Assert.Equal(id, deserialized);
    }

    [Fact]
    public void TypeConverter_RoundTripsAsPlainString()
    {
        var converter = TypeDescriptor.GetConverter(typeof(InvoiceConfigurationId));
        var id = new InvoiceConfigurationId("m365-copilot");

        Assert.Equal("m365-copilot", converter.ConvertToString(id));
        Assert.Equal(id, converter.ConvertFromString("m365-copilot"));
    }
}
