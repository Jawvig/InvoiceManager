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
        var recordRepo = new InMemoryInvoiceRecordRepository();
        var function = BuildTimerFunction(recordRepo, config1, config2);

        await function.RunAsync(new TimerInfo(), CancellationToken.None);

        Assert.Equal(2, recordRepo.All.Count);
        Assert.Contains(recordRepo.All, r => r.ConfigurationId == config1.Id && r.ExpectedDate == new DateOnly(2025, 7, 1));
        Assert.Contains(recordRepo.All, r => r.ConfigurationId == config2.Id && r.ExpectedDate == new DateOnly(2025, 8, 1));
    }

    [Fact]
    public async Task TimerFunction_DoesNotCreateRecords_WhenNoActiveConfigurationsExist()
    {
        var recordRepo = new InMemoryInvoiceRecordRepository();
        var function = BuildTimerFunction(recordRepo);

        await function.RunAsync(new TimerInfo(), CancellationToken.None);

        Assert.Empty(recordRepo.All);
    }

    [Fact]
    public async Task TimerFunction_ProcessesRemainingConfigurations_WhenOneFails()
    {
        var failing = BuildConfig("config-failing", new DateOnly(2025, 7, 1));
        var healthy = BuildConfig("config-healthy", new DateOnly(2025, 8, 1));
        var recordRepo = new InMemoryInvoiceRecordRepository(failFor: failing.Id);
        var function = BuildTimerFunction(recordRepo, failing, healthy);

        await function.RunAsync(new TimerInfo(), CancellationToken.None);

        var record = Assert.Single(recordRepo.All);
        Assert.Equal(healthy.Id, record.ConfigurationId);
    }

    private static GenerateExpectedRecordsTimer BuildTimerFunction(
        IInvoiceRecordRepository recordRepo,
        params InvoiceConfiguration[] configurations)
    {
        var generator = new ExpectedRecordGenerator(
            recordRepo,
            new FakeConfigurationRepository(configurations),
            NullLogger<ExpectedRecordGenerator>.Instance);
        return new GenerateExpectedRecordsTimer(
            generator,
            NullLogger<GenerateExpectedRecordsTimer>.Instance);
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

    private sealed class InMemoryInvoiceRecordRepository(InvoiceConfigurationId? failFor = null) : IInvoiceRecordRepository
    {
        private readonly List<InvoiceRecord> store = [];

        public IReadOnlyList<InvoiceRecord> All => store;

        public Task<Option<InvoiceRecord>> GetMostRecentAsync(
            InvoiceConfigurationId configurationId,
            CancellationToken cancellationToken = default)
        {
            if (configurationId == failFor)
                throw new InvalidOperationException("Simulated repository failure.");

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
