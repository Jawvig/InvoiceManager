using System.Text.Json;
using InvoiceManager.AdminWeb.Pages.Configurations;
using InvoiceManager.Core;
using InvoiceManager.TestSupport;

namespace InvoiceManager.AdminWeb.Tests;

public sealed class InvoiceConfigurationExportTests
{
    [Fact]
    public void FromConfiguration_ExcludesCosmosAndEnvironmentLocalFields()
    {
        var configuration = Configurations.Build();

        var json = JsonSerializer.Serialize(
            InvoiceConfigurationExport.FromConfiguration(configuration), InvoiceConfigurationExportJson.Options);
        using var document = JsonDocument.Parse(json);
        var propertyNames = document.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();

        // Cosmos document envelope fields (InvoiceConfigurationDocument) must never appear.
        Assert.DoesNotContain("documentType", propertyNames);
        Assert.DoesNotContain("partitionKey", propertyNames);
        Assert.DoesNotContain("_etag", propertyNames);
        Assert.DoesNotContain("etag", propertyNames);
        // Environment-local state that shouldn't carry across a promote: isActive defaults to
        // inactive on import via ConfigurationFormInput.Build(false, ...), so it isn't exported.
        Assert.DoesNotContain("isActive", propertyNames);
    }

    [Fact]
    public void FromConfiguration_ThenToFormInput_RoundTripsMicrosoftBillingFields()
    {
        var configuration = Configurations.Build(
            id: new InvoiceConfigurationId("promoted-config"),
            invoiceDescription: "Promoted invoice",
            integrationConfiguration: new MicrosoftBillingIntegrationConfiguration("billing-account-1"),
            oneDriveFolder: new OneDriveFolder("drive-1", "Drive One", "folder-1", "/Bills/Promoted"));

        var export = InvoiceConfigurationExport.FromConfiguration(configuration);
        var json = JsonSerializer.Serialize(export, InvoiceConfigurationExportJson.Options);
        var roundTripped = JsonSerializer.Deserialize<InvoiceConfigurationExport>(json, InvoiceConfigurationExportJson.Options)!;
        var input = roundTripped.ToFormInput();

        Assert.Equal("promoted-config", input.Id);
        Assert.Equal(IntegrationType.MicrosoftBilling, input.IntegrationType);
        Assert.Equal("Promoted invoice", input.InvoiceDescription);
        Assert.Equal("billing-account-1", input.BillingAccountId);
        Assert.Equal("drive-1", input.DriveId);
        Assert.Equal("Drive One", input.DriveName);
        Assert.Equal("folder-1", input.FolderItemId);
        Assert.Equal("/Bills/Promoted", input.FolderPath);
        Assert.True(input.HasExpectedAmount);
        Assert.Equal(10.00m, input.ExpectedAmount);
        Assert.Equal("GBP", input.Currency);

        var rebuilt = input.Build(
            false,
            [new InvoiceManager.Infrastructure.MicrosoftAuthorization.BillingAccountChoice("billing-account-1", "Name", "Business")],
            currentBillingAccountId: null,
            new OneDriveFolder("drive-1", "Drive One", "folder-1", "/Bills/Promoted"));

        Assert.Equal(configuration.InvoiceDescription, rebuilt.InvoiceDescription);
        Assert.False(rebuilt.IsActive);
    }

    [Fact]
    public void FromConfiguration_ThenToFormInput_RoundTripsGraphEmailFields()
    {
        var configuration = Configurations.Build(
            integrationConfiguration: new GraphEmailIntegrationConfiguration("billing@example.com", "Invoice \\d+"));

        var export = InvoiceConfigurationExport.FromConfiguration(configuration);
        var input = export.ToFormInput();

        Assert.Equal(IntegrationType.GraphEmail, input.IntegrationType);
        Assert.Equal("billing@example.com", input.SenderEmailAddress);
        Assert.Equal("Invoice \\d+", input.BodyPattern);
        Assert.Equal("", input.BillingAccountId);
    }

    [Fact]
    public void ToFormInput_WithoutAmountMatching_LeavesHasExpectedAmountFalse()
    {
        var configuration = Configurations.Build(amountTolerance: 0m) with { AmountMatchingCriteria = Option.None };

        var input = InvoiceConfigurationExport.FromConfiguration(configuration).ToFormInput();

        Assert.False(input.HasExpectedAmount);
        Assert.Null(input.ExpectedAmount);
    }

    [Fact]
    public void Deserialize_RejectsFileMissingRequiredFields()
    {
        var incomplete = """{ "id": "only-an-id" }""";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<InvoiceConfigurationExport>(incomplete, InvoiceConfigurationExportJson.Options));
    }
}
