using InvoiceManager.Core.Repositories;

namespace InvoiceManager.Core.Tests;

public sealed class ExpectedRecordGeneratorTests
{
    // Cycle 1: no records exist → creates expected record with config start date.
    [Fact]
    public async Task GenerateAsync_CreatesExpectedRecord_WhenNoRecordsExist()
    {
        var config = Configurations.Build(startDate: new DateOnly(2025, 7, 1));
        var records = new InMemoryInvoiceRecordRepository();
        var generator = new ExpectedRecordGenerator(records);

        await generator.GenerateAsync(config);

        var record = Assert.Single(records.All);
        Assert.Equal(config.Id, record.ConfigurationId);
        Assert.Equal(new DateOnly(2025, 7, 1), record.ExpectedDate);
        Assert.True(record.State is Expected);
        Assert.Equal(config.InvoiceDescription, record.InvoiceDescription);
        Assert.Equal(config.DefaultExpectedAmount, record.ExpectedAmount);
        Assert.Equal(config.DefaultVatMode, record.ExpectedVatMode);
        Assert.Equal(config.DateToleranceDays, record.DateToleranceDays);
    }

    // Cycle 2: most recent record is SavedToOneDrive → creates next expected record using actual date + frequency.
    [Fact]
    public async Task GenerateAsync_CreatesNextExpectedRecord_WhenMostRecentIsSavedToOneDrive()
    {
        var config = Configurations.Build(startDate: new DateOnly(2025, 7, 1));
        var existing = Records.Build(config,
            expectedDate: new DateOnly(2025, 7, 1),
            state: new SavedToOneDrive(
                new ActualInvoiceDetails(new DateOnly(2025, 7, 5)),
                new OneDriveDetails("/drives/test/root:/Bills/Test/invoice.pdf")));
        var records = new InMemoryInvoiceRecordRepository(existing);
        var generator = new ExpectedRecordGenerator(records);

        await generator.GenerateAsync(config);

        Assert.Equal(2, records.All.Count);
        var next = records.All.Single(r => r.State is Expected);
        Assert.Equal(new DateOnly(2025, 8, 5), next.ExpectedDate);
    }

    // Cycle 3: most recent record is in progress (before SavedToOneDrive) → does nothing.
    [Fact]
    public async Task GenerateAsync_DoesNothing_WhenMostRecentIsInProgress()
    {
        var config = Configurations.Build();
        var existing = Records.Build(config, state: new Retrieved(
            new ActualInvoiceDetails(new DateOnly(2025, 7, 5))));
        var records = new InMemoryInvoiceRecordRepository(existing);
        var generator = new ExpectedRecordGenerator(records);

        await generator.GenerateAsync(config);

        Assert.Single(records.All);
    }

    // Cycle 4: expected record already exists for the calculated date → idempotent, does nothing.
    [Fact]
    public async Task GenerateAsync_DoesNothing_WhenExpectedRecordAlreadyExistsForDate()
    {
        var config = Configurations.Build(startDate: new DateOnly(2025, 7, 1));
        var existing = Records.Build(config,
            expectedDate: new DateOnly(2025, 7, 1),
            state: new Expected());
        var records = new InMemoryInvoiceRecordRepository(existing);
        var generator = new ExpectedRecordGenerator(records);

        await generator.GenerateAsync(config);

        Assert.Single(records.All);
    }

    private sealed class InMemoryInvoiceRecordRepository : IInvoiceRecordRepository
    {
        private readonly List<InvoiceRecord> store;

        public InMemoryInvoiceRecordRepository(params InvoiceRecord[] initial)
        {
            store = [.. initial];
        }

        public IReadOnlyList<InvoiceRecord> All => store;

        public Task<Option<InvoiceRecord>> GetMostRecentAsync(
            InvoiceConfigurationId configurationId,
            CancellationToken cancellationToken = default)
        {
            var record = store
                .Where(r => r.ConfigurationId == configurationId)
                .OrderByDescending(r => r.ExpectedDate)
                .FirstOrDefault();

            Option<InvoiceRecord> result = record is not null ? record : Option.None;
            return Task.FromResult(result);
        }

        public Task CreateIfNotExistsAsync(InvoiceRecord record, CancellationToken cancellationToken = default)
        {
            if (!store.Any(r => r.Id == record.Id))
                store.Add(record);
            return Task.CompletedTask;
        }
    }
}
