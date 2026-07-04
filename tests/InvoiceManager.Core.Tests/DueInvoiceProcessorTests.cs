using System.Globalization;
using InvoiceManager.Core.Integrations;
using InvoiceManager.Core.Repositories;
using InvoiceManager.TestSupport;
using Microsoft.Extensions.Logging;
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
    public async Task ProcessDueAsync_ResumesRetrievedRecord_OnLaterRun()
    {
        var config = Configurations.Build(startDate: new DateOnly(2025, 7, 10));
        var actualDetails = Actuals.Build(
            new DateOnly(2025, 7, 12),
            new Money(10.00m, "GBP"),
            new SourceInvoiceId("G152207778"));
        var retrievedRecord = Records.Build(
            config,
            expectedDate: new DateOnly(2025, 7, 10),
            state: new Retrieved(actualDetails));
        var records = new InMemoryInvoiceRecordRepository(retrievedRecord);

        var source = new FakeInvoiceSourceIntegration(new InvoiceMatch([1, 2, 3], actualDetails));
        var oneDrive = new FakeOneDriveIntegration();
        var processor = BuildProcessor(records, source, oneDrive, config);

        var results = await processor.ProcessDueAsync();

        var success = Assert.Single(results);
        Assert.True(success is ProcessingSucceeded succeeded && succeeded.RecordId == retrievedRecord.Id);

        var saved = records.All.Single(r => r.Id == retrievedRecord.Id);
        Assert.True(saved.State is SavedToOneDrive, $"Expected SavedToOneDrive but was {saved.State}.");
        Assert.Single(oneDrive.Uploads);

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
    public async Task ProcessDueAsync_MarksNotYetFound_WhenNoMatchWithinToleranceWindow()
    {
        // Expected 2025-07-14 + 5 day tolerance = deadline 2025-07-19, still ahead of today (2025-07-15).
        var config = Configurations.Build(startDate: new DateOnly(2025, 7, 14));
        var dueRecord = Records.Build(config, expectedDate: new DateOnly(2025, 7, 14));
        var records = new InMemoryInvoiceRecordRepository(dueRecord);
        var oneDrive = new FakeOneDriveIntegration();

        var processor = BuildProcessor(records, new FakeInvoiceSourceIntegration(new NoInvoiceMatch()), oneDrive, config);

        var results = await processor.ProcessDueAsync();

        Assert.True(Assert.Single(results) is ProcessingNotYetFound);
        Assert.True(records.All.Single().State is NotYetFound);
        Assert.Empty(oneDrive.Uploads);
    }

    [Fact]
    public async Task ProcessDueAsync_MarksNotFound_WhenNoMatchOnToleranceDeadline()
    {
        // Expected 2025-07-10 + 5 day tolerance = deadline 2025-07-15, which is today: on or after → NotFound.
        var config = Configurations.Build(startDate: new DateOnly(2025, 7, 10));
        var dueRecord = Records.Build(config, expectedDate: new DateOnly(2025, 7, 10));
        var records = new InMemoryInvoiceRecordRepository(dueRecord);
        var oneDrive = new FakeOneDriveIntegration();

        var processor = BuildProcessor(records, new FakeInvoiceSourceIntegration(new NoInvoiceMatch()), oneDrive, config);

        var results = await processor.ProcessDueAsync();

        Assert.True(Assert.Single(results) is ProcessingNotFound);
        Assert.True(records.All.Single().State is NotFound);
        Assert.Empty(oneDrive.Uploads);
    }

    [Fact]
    public async Task ProcessDueAsync_MarksNotFoundDirectly_WhenFirstProcessedAfterToleranceWindow()
    {
        // Expected 2025-07-01 + 5 day tolerance = deadline 2025-07-06, already elapsed by today (2025-07-15).
        // A still-Expected record processed for the first time after its window goes straight to NotFound.
        var config = Configurations.Build(startDate: new DateOnly(2025, 7, 1));
        var dueRecord = Records.Build(config, expectedDate: new DateOnly(2025, 7, 1), state: new Expected());
        var records = new InMemoryInvoiceRecordRepository(dueRecord);

        var processor = BuildProcessor(records, new FakeInvoiceSourceIntegration(new NoInvoiceMatch()), new FakeOneDriveIntegration(), config);

        var results = await processor.ProcessDueAsync();

        Assert.True(Assert.Single(results) is ProcessingNotFound);
        Assert.True(records.All.Single().State is NotFound);
    }

    [Fact]
    public async Task ProcessDueAsync_MarksRetrievalError_WhenRetrievalThrows()
    {
        var config = Configurations.Build(startDate: new DateOnly(2025, 7, 10));
        var dueRecord = Records.Build(config, expectedDate: new DateOnly(2025, 7, 10));
        var records = new InMemoryInvoiceRecordRepository(dueRecord);

        var source = new ThrowingSourceIntegration(
            failFor: new DateOnly(2025, 7, 10),
            otherwise: new NoInvoiceMatch());
        var processor = BuildProcessor(records, source, new FakeOneDriveIntegration(), config);

        var results = await processor.ProcessDueAsync();

        Assert.True(Assert.Single(results) is ProcessingFailed);
        var stored = records.All.Single();
        if (stored.State is not RetrievalError error)
        {
            Assert.Fail($"Expected RetrievalError but was {stored.State}.");
            return;
        }

        Assert.Equal("Simulated source failure.", error.ErrorMessage);
    }

    [Fact]
    public async Task ProcessDueAsync_RetriesRetrievalErrorRecord_AndSavesOnLaterMatch()
    {
        var config = Configurations.Build(startDate: new DateOnly(2025, 7, 10));
        var erroredRecord = Records.Build(
            config,
            expectedDate: new DateOnly(2025, 7, 10),
            state: new RetrievalError("earlier transient failure"));
        var records = new InMemoryInvoiceRecordRepository(erroredRecord);

        var match = BuildMatch(new DateOnly(2025, 7, 12), new Money(10.00m, "GBP"), "G152207778");
        var oneDrive = new FakeOneDriveIntegration();
        var processor = BuildProcessor(records, new FakeInvoiceSourceIntegration(match), oneDrive, config);

        var results = await processor.ProcessDueAsync();

        Assert.True(Assert.Single(results) is ProcessingSucceeded);
        Assert.True(records.All.Single(r => r.Id == erroredRecord.Id).State is SavedToOneDrive);
        Assert.Single(oneDrive.Uploads);
    }

    [Fact]
    public async Task ProcessDueAsync_LogsRunSummaryWithPerOutcomeCounts()
    {
        var savedConfig = Configurations.Build(id: new InvoiceConfigurationId("config-saved"), startDate: new DateOnly(2025, 7, 10));
        var notFoundConfig = Configurations.Build(id: new InvoiceConfigurationId("config-notfound"), startDate: new DateOnly(2025, 7, 1));
        var savedRecord = Records.Build(savedConfig, expectedDate: new DateOnly(2025, 7, 10));
        var notFoundRecord = Records.Build(notFoundConfig, expectedDate: new DateOnly(2025, 7, 1));
        var records = new InMemoryInvoiceRecordRepository(savedRecord, notFoundRecord);

        var source = new DateDrivenSourceIntegration(
            matches: new Dictionary<DateOnly, InvoiceSourceResult>
            {
                [new DateOnly(2025, 7, 10)] = BuildMatch(new DateOnly(2025, 7, 12), new Money(10.00m, "GBP"), "SRC-1"),
                [new DateOnly(2025, 7, 1)] = new NoInvoiceMatch(),
            });
        var logger = new ListLogger<DueInvoiceProcessor>();

        var processor = new DueInvoiceProcessor(
            records,
            new FakeConfigurationRepository(savedConfig, notFoundConfig),
            [source],
            new FakeOneDriveIntegration(),
            BuildFilename(),
            BuildGenerator(records, savedConfig, notFoundConfig),
            new FixedTimeProvider(Today),
            logger);

        await processor.ProcessDueAsync();

        var summary = Assert.Single(logger.Messages, m => m.Contains("run complete"));
        Assert.Contains("1 saved", summary);
        Assert.Contains("0 not yet found", summary);
        Assert.Contains("1 not found", summary);
        Assert.Contains("0 failed", summary);
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
        Assert.True(records.All.Single(r => r.Id == failingRecord.Id).State is RetrievalError);
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

    /// <summary>Returns a preconfigured result per expected date, for multi-record runs.</summary>
    private sealed class DateDrivenSourceIntegration(IReadOnlyDictionary<DateOnly, InvoiceSourceResult> matches)
        : IInvoiceSourceIntegration
    {
        public IntegrationType IntegrationType => IntegrationType.Microsoft365;

        public Task<InvoiceSourceResult> FindInvoiceAsync(
            InvoiceSearchCriteria criteria,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(matches[criteria.ExpectedDate]);
    }

    /// <summary>Captures rendered log messages for asserting emitted telemetry.</summary>
    private sealed class ListLogger<T> : ILogger<T>
    {
        private readonly List<string> messages = [];

        public IReadOnlyList<string> Messages => messages;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            messages.Add(formatter(state, exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
