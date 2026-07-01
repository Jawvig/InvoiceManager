using InvoiceManager.Core;
using InvoiceManager.Core.Repositories;
using InvoiceManager.Functions.Functions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using NodaMoney;

namespace InvoiceManager.Functions.Tests;

public sealed class GenerateExpectedRecordsFunctionTests
{
    [Fact]
    public async Task TimerFunction_CreatesExpectedRecords_ForEachActiveConfiguration()
    {
        var config1 = BuildConfig("config-1", new DateOnly(2025, 7, 1));
        var config2 = BuildConfig("config-2", new DateOnly(2025, 8, 1));
        var configRepo = new FakeConfigurationRepository(config1, config2);
        var recordRepo = new InMemoryInvoiceRecordRepository();
        var generator = new ExpectedRecordGenerator(recordRepo);
        var function = new GenerateExpectedRecordsTimer(
            configRepo, generator,
            NullLogger<GenerateExpectedRecordsTimer>.Instance);

        await function.RunAsync(new TimerInfo(), CancellationToken.None);

        Assert.Equal(2, recordRepo.All.Count);
        Assert.Contains(recordRepo.All, r => r.ConfigurationId == config1.Id && r.ExpectedDate == new DateOnly(2025, 7, 1));
        Assert.Contains(recordRepo.All, r => r.ConfigurationId == config2.Id && r.ExpectedDate == new DateOnly(2025, 8, 1));
    }

    [Fact]
    public async Task TimerFunction_DoesNotCreateRecords_WhenNoActiveConfigurationsExist()
    {
        var configRepo = new FakeConfigurationRepository();
        var recordRepo = new InMemoryInvoiceRecordRepository();
        var generator = new ExpectedRecordGenerator(recordRepo);
        var function = new GenerateExpectedRecordsTimer(
            configRepo, generator,
            NullLogger<GenerateExpectedRecordsTimer>.Instance);

        await function.RunAsync(new TimerInfo(), CancellationToken.None);

        Assert.Empty(recordRepo.All);
    }

    private static InvoiceConfiguration BuildConfig(string id, DateOnly startDate) =>
        new(
            new InvoiceConfigurationId(id),
            IntegrationType.Microsoft365,
            "Test Invoice",
            InvoiceFrequency.Monthly,
            new Money(10.00m, "GBP"),
            VatMode.Exclusive,
            IsActive: true,
            OneDriveDestination: "/drives/test/root:/Bills/Test",
            StartDate: startDate,
            BillingAccountId: "test:billing:account",
            DateToleranceDays: 5);

    private sealed class FakeConfigurationRepository(params InvoiceConfiguration[] configurations) : IInvoiceConfigurationRepository
    {
        public Task<IReadOnlyList<InvoiceConfiguration>> ListActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InvoiceConfiguration>>(
                configurations.Where(c => c.IsActive).ToList());

        public Task CreateIfNotExistsAsync(InvoiceConfiguration configuration, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class InMemoryInvoiceRecordRepository : IInvoiceRecordRepository
    {
        private readonly List<InvoiceRecord> store = [];

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

        public Task<bool> ExistsAsync(
            InvoiceConfigurationId configurationId,
            DateOnly expectedDate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(store.Any(r => r.ConfigurationId == configurationId && r.ExpectedDate == expectedDate));

        public Task CreateAsync(InvoiceRecord record, CancellationToken cancellationToken = default)
        {
            store.Add(record);
            return Task.CompletedTask;
        }
    }
}
