using System.Text.Json;
using InvoiceManager.Core;
using InvoiceManager.Infrastructure.CosmosDb;
using NodaMoney;

namespace InvoiceManager.Infrastructure.Tests;

public sealed class InvoiceRecordDocumentTests
{
    private const string OneDriveLocation = "/drives/test/root:/Bills/Test/invoice.pdf";

    [Fact]
    public void RoundTrip_PreservesRecord_WhenStateIsExpected()
    {
        var record = BuildRecord(new Expected());

        var roundTripped = InvoiceRecordDocument.FromRecord(record).ToRecord();

        Assert.Equal(record, roundTripped);
    }

    [Fact]
    public void RoundTrip_PreservesRecord_WhenStateIsRetrieved()
    {
        var record = BuildRecord(new Retrieved(
            new ActualInvoiceDetails(new DateOnly(2025, 7, 5))));

        var roundTripped = InvoiceRecordDocument.FromRecord(record).ToRecord();

        Assert.Equal(record, roundTripped);
    }

    [Fact]
    public void RoundTrip_PreservesRecord_WhenStateIsReconciledFromOneDrive()
    {
        var record = BuildRecord(new ReconciledFromOneDrive(
            new ActualInvoiceDetails(new DateOnly(2025, 7, 5)),
            new OneDriveDetails(OneDriveLocation)));

        var roundTripped = InvoiceRecordDocument.FromRecord(record).ToRecord();

        Assert.Equal(record, roundTripped);
    }

    [Fact]
    public void RoundTrip_PreservesRecord_WhenStateIsSavedToOneDrive()
    {
        var record = BuildRecord(new SavedToOneDrive(
            new ActualInvoiceDetails(new DateOnly(2025, 7, 5)),
            new OneDriveDetails(OneDriveLocation)));

        var roundTripped = InvoiceRecordDocument.FromRecord(record).ToRecord();

        Assert.Equal(record, roundTripped);
    }

    [Fact]
    public void ToRecord_Throws_WhenPayloadStatusIsMissingActualInvoiceDetails()
    {
        var document = BuildDocument(status: "Retrieved");

        var ex = Assert.Throws<InvalidOperationException>(() => document.ToRecord());
        Assert.Equal(
            "Invoice record document 'config-1_2025-07-01' has status 'Retrieved' " +
            "but is missing 'actualInvoiceDetails'.",
            ex.Message);
    }

    [Fact]
    public void ToRecord_Throws_WhenPayloadStatusIsMissingOneDriveDetails()
    {
        var document = BuildDocument(
            status: "SavedToOneDrive",
            actualDetails: new ActualInvoiceDetailsDocument { ActualInvoiceDate = "2025-07-05" });

        var ex = Assert.Throws<InvalidOperationException>(() => document.ToRecord());
        Assert.Equal(
            "Invoice record document 'config-1_2025-07-01' has status 'SavedToOneDrive' " +
            "but is missing 'oneDriveDetails'.",
            ex.Message);
    }

    [Fact]
    public void ToRecord_Throws_WhenStatusIsUnrecognised()
    {
        var document = BuildDocument(status: "Teleported");

        var ex = Assert.Throws<InvalidOperationException>(() => document.ToRecord());
        Assert.Equal(
            "Invoice record document 'config-1_2025-07-01' has unrecognised status 'Teleported'.",
            ex.Message);
    }

    [Fact]
    public void Deserialize_Throws_WhenActualInvoiceDetailsIsMissingItsRequiredProperty()
    {
        const string json = """
            {
              "id": "config-1_2025-07-01",
              "configurationId": "config-1",
              "invoiceDescription": "Test Invoice",
              "expectedDate": "2025-07-01",
              "dateToleranceDays": 5,
              "expectedAmount": 10.00,
              "expectedCurrency": "GBP",
              "expectedVatMode": "Exclusive",
              "status": "Retrieved",
              "actualInvoiceDetails": {}
            }
            """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<InvoiceRecordDocument>(json));
    }

    private static InvoiceRecord BuildRecord(InvoiceWorkflowState state) =>
        new(
            new InvoiceConfigurationId("config-1"),
            "Test Invoice",
            new DateOnly(2025, 7, 1),
            DateToleranceDays: 5,
            new Money(10.00m, "GBP"),
            VatMode.Exclusive,
            state);

    private static InvoiceRecordDocument BuildDocument(
        string status,
        ActualInvoiceDetailsDocument? actualDetails = null,
        OneDriveDetailsDocument? oneDriveDetails = null) =>
        new()
        {
            Id = "config-1_2025-07-01",
            ConfigurationId = "config-1",
            InvoiceDescription = "Test Invoice",
            ExpectedDate = "2025-07-01",
            DateToleranceDays = 5,
            ExpectedAmount = 10.00m,
            ExpectedCurrency = "GBP",
            ExpectedVatMode = "Exclusive",
            Status = status,
            ActualInvoiceDetails = actualDetails,
            OneDriveDetails = oneDriveDetails,
        };
}
