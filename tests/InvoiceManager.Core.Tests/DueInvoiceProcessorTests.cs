using System.Globalization;
using InvoiceManager.Core.Integrations;
using InvoiceManager.Core.Repositories;
using InvoiceManager.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NodaMoney;

namespace InvoiceManager.Core.Tests;

public sealed class DueInvoiceProcessorTests
{
    private static readonly DateOnly Today = new(2025, 7, 15);

    [Fact]
    public async Task ProcessDueAsync_DrivesRecordThroughRetrievedThenSavedToOneDrive_OnMatch()
    {
        var config = Configurations.Build(startDate: new DateOnly(2025, 7, 10));
        var dueRecord = Records.Build(config, expectedDate: new DateOnly(2025, 7, 10));
        var records = new InMemoryInvoiceRecordRepository(dueRecord);

        var match = BuildMatch(new DateOnly(2025, 7, 12), new Money(10.00m, "GBP"), "G152207778");
        var source = new FakeInvoiceSourceIntegration(match);
        var oneDrive = new FakeOneDriveIntegration();

        var processor = BuildProcessor(records, source, oneDrive, config);

        var results = await processor.ProcessDueAsync();

        var success = Assert.Single(results);
        Assert.True(success is ProcessingSucceeded succeeded && succeeded.RecordId == dueRecord.Id);

        var saved = records.All.Single(r => r.Id == dueRecord.Id);
        if (saved.State is not SavedToOneDrive savedState)
        {
            Assert.Fail($"Expected SavedToOneDrive but was {saved.State}.");
            return;
        }

        Assert.Equal(new DateOnly(2025, 7, 12), savedState.ActualDetails.ActualInvoiceDate);
        Assert.Equal(new SourceInvoiceId("G152207778"), savedState.ActualDetails.SourceInvoiceId);
        Assert.Equal(
            "/drives/test/root:/Bills/Test/2025-07-12 Test Invoice G152207778 £10.00 exc.pdf",
            savedState.OneDriveDetails.OneDriveLocation);
    }

    [Fact]
    public async Task ProcessDueAsync_UploadsGeneratedFilenameAndPdfBytes_OnMatch()
    {
        var config = Configurations.Build(startDate: new DateOnly(2025, 7, 10));
        var dueRecord = Records.Build(config, expectedDate: new DateOnly(2025, 7, 10));
        var records = new InMemoryInvoiceRecordRepository(dueRecord);

        var pdf = new byte[] { 1, 2, 3, 4 };
        var match = new InvoiceMatch(
            pdf,
            Actuals.Build(new DateOnly(2025, 7, 12), new Money(10.00m, "GBP"), new SourceInvoiceId("G152207778")));
        var oneDrive = new FakeOneDriveIntegration();

        var processor = BuildProcessor(records, new FakeInvoiceSourceIntegration(match), oneDrive, config);

        await processor.ProcessDueAsync();

        var upload = Assert.Single(oneDrive.Uploads);
        Assert.Equal("/drives/test/root:/Bills/Test", upload.DestinationPath);
        Assert.Equal("2025-07-12 Test Invoice G152207778 £10.00 exc.pdf", upload.FileName);
        Assert.Equal(pdf, upload.Content);
    }

    [Fact]
    public async Task ProcessDueAsync_PersistsAfterEachStep_OnMatch()
    {
        var config = Configurations.Build(startDate: new DateOnly(2025, 7, 10));
        var dueRecord = Records.Build(config, expectedDate: new DateOnly(2025, 7, 10));
        var records = new RecordingInvoiceRecordRepository(dueRecord);

        var match = BuildMatch(new DateOnly(2025, 7, 12), new Money(10.00m, "GBP"), "G152207778");
        var processor = BuildProcessor(records, new FakeInvoiceSourceIntegration(match), new FakeOneDriveIntegration(), config);

        await processor.ProcessDueAsync();

        // Retrieved is persisted before the upload, SavedToOneDrive after it.
        Assert.Collection(
            records.Replaced,
            first => Assert.True(first.State is Retrieved, $"Expected Retrieved but was {first.State}."),
            second => Assert.True(second.State is SavedToOneDrive, $"Expected SavedToOneDrive but was {second.State}."));
    }

    [Fact]
    public async Task ProcessDueAsync_CreatesNextExpectedRecord_OnSuccess()
    {
        var config = Configurations.Build(startDate: new DateOnly(2025, 7, 10));
        var dueRecord = Records.Build(config, expectedDate: new DateOnly(2025, 7, 10));
        var records = new InMemoryInvoiceRecordRepository(dueRecord);

        var match = BuildMatch(new DateOnly(2025, 7, 12), new Money(10.00m, "GBP"), "G152207778");
        var processor = BuildProcessor(records, new FakeInvoiceSourceIntegration(match), new FakeOneDriveIntegration(), config);

        await processor.ProcessDueAsync();

        // Next expected date = actual invoice date + monthly frequency.
        var next = records.All.Single(r => r.State is Expected);
        Assert.Equal(new DateOnly(2025, 8, 12), next.ExpectedDate);
    }

    [Fact]
    public async Task ProcessDueAsync_BuildsCriteriaFromRecordAndConfiguration()
    {
        var config = Configurations.Build(startDate: new DateOnly(2025, 7, 10), amountTolerance: 0.50m);
        var dueRecord = Records.Build(config, expectedDate: new DateOnly(2025, 7, 10));
        var records = new InMemoryInvoiceRecordRepository(dueRecord);

        var source = new FakeInvoiceSourceIntegration(new NoInvoiceMatch());
        var processor = BuildProcessor(records, source, new FakeOneDriveIntegration(), config);

        await processor.ProcessDueAsync();

        var criteria = Assert.Single(source.Requests);
        Assert.Equal(config.BillingAccountId, criteria.BillingAccountId);
        Assert.Equal(new DateOnly(2025, 7, 10), criteria.ExpectedDate);
        Assert.Equal(config.DateToleranceDays, criteria.DateToleranceDays);
        Assert.Equal(config.DefaultExpectedAmount, criteria.ExpectedAmount);
        Assert.Equal(0.50m, criteria.AmountTolerance);
    }

    [Fact]
    public async Task ProcessDueAsync_LeavesRecordExpected_OnNoMatch()
    {
        var config = Configurations.Build(startDate: new DateOnly(2025, 7, 10));
        var dueRecord = Records.Build(config, expectedDate: new DateOnly(2025, 7, 10));
        var records = new InMemoryInvoiceRecordRepository(dueRecord);
        var oneDrive = new FakeOneDriveIntegration();

        var processor = BuildProcessor(records, new FakeInvoiceSourceIntegration(new NoInvoiceMatch()), oneDrive, config);

        var results = await processor.ProcessDueAsync();

        Assert.True(Assert.Single(results) is ProcessingSkippedNoMatch);
        Assert.True(records.All.Single().State is Expected);
        Assert.Empty(oneDrive.Uploads);
    }

    [Fact]
    public async Task ProcessDueAsync_IsolatesFailure_AndContinuesWithOtherRecords()
    {
        var failing = Configurations.Build(id: new InvoiceConfigurationId("config-failing"), startDate: new DateOnly(2025, 7, 1));
        var healthy = Configurations.Build(id: new InvoiceConfigurationId("config-healthy"), startDate: new DateOnly(2025, 7, 2));
        var failingRecord = Records.Build(failing, expectedDate: new DateOnly(2025, 7, 1));
        var healthyRecord = Records.Build(healthy, expectedDate: new DateOnly(2025, 7, 2));
        var records = new InMemoryInvoiceRecordRepository(failingRecord, healthyRecord);

        var source = new ThrowingSourceIntegration(
            failFor: new DateOnly(2025, 7, 1),
            otherwise: BuildMatch(new DateOnly(2025, 7, 3), new Money(10.00m, "GBP"), "SRC-1"));

        var processor = new DueInvoiceProcessor(
            records,
            new FakeConfigurationRepository(failing, healthy),
            [source],
            new FakeOneDriveIntegration(),
            BuildFilename(),
            BuildGenerator(records, failing, healthy),
            new FixedTimeProvider(Today),
            NullLogger<DueInvoiceProcessor>.Instance);

        var results = await processor.ProcessDueAsync();

        Assert.Equal(2, results.Count);
        var failure = Assert.Single(results, r => r is ProcessingFailed);
        Assert.True(failure is ProcessingFailed failed && failed.RecordId == failingRecord.Id);
        Assert.Single(results, r => r is ProcessingSucceeded);
    }

    private static InvoiceMatch BuildMatch(DateOnly date, Money amount, string sourceInvoiceId) =>
        new([1, 2, 3], Actuals.Build(date, amount, new SourceInvoiceId(sourceInvoiceId)));

    private static DueInvoiceProcessor BuildProcessor(
        IInvoiceRecordRepository records,
        IInvoiceSourceIntegration source,
        IOneDriveIntegration oneDrive,
        params InvoiceConfiguration[] configurations) =>
        new(
            records,
            new FakeConfigurationRepository(configurations),
            [source],
            oneDrive,
            BuildFilename(),
            BuildGenerator(records, configurations),
            new FixedTimeProvider(Today),
            NullLogger<DueInvoiceProcessor>.Instance);

    private static ExpectedRecordGenerator BuildGenerator(
        IInvoiceRecordRepository records,
        params InvoiceConfiguration[] configurations) =>
        new(records, new FakeConfigurationRepository(configurations), NullLogger<ExpectedRecordGenerator>.Instance);

    private static InvoiceFilename BuildFilename() =>
        new(new InvoiceFilenameSettings { Culture = CultureInfo.GetCultureInfo("en-GB") });

    /// <summary>Records the order of <see cref="ReplaceAsync"/> calls for step-persistence assertions.</summary>
    private sealed class RecordingInvoiceRecordRepository(params InvoiceRecord[] initial)
        : InMemoryInvoiceRecordRepository(initial)
    {
        private readonly List<InvoiceRecord> replaced = [];

        public IReadOnlyList<InvoiceRecord> Replaced => replaced;

        public override Task ReplaceAsync(InvoiceRecord record, CancellationToken cancellationToken = default)
        {
            replaced.Add(record);
            return base.ReplaceAsync(record, cancellationToken);
        }
    }

    /// <summary>Throws for a specific expected date, matching otherwise, to exercise failure isolation.</summary>
    private sealed class ThrowingSourceIntegration(DateOnly failFor, InvoiceSourceResult otherwise)
        : IInvoiceSourceIntegration
    {
        public IntegrationType IntegrationType => IntegrationType.Microsoft365;

        public Task<InvoiceSourceResult> FindInvoiceAsync(
            InvoiceSearchCriteria criteria,
            CancellationToken cancellationToken = default) =>
            criteria.ExpectedDate == failFor
                ? throw new InvalidOperationException("Simulated source failure.")
                : Task.FromResult(otherwise);
    }
}
