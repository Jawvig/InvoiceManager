using InvoiceManager.Core;
using InvoiceManager.Core.Repositories;
using InvoiceManager.Functions.Functions;
using InvoiceManager.TestSupport;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;

namespace InvoiceManager.Functions.Tests;

public sealed class GenerateExpectedRecordsFunctionTests
{
    [Fact]
    public async Task TimerFunction_CreatesExpectedRecords_ForEachActiveConfiguration()
    {
        var config1 = Configurations.Build(id: new InvoiceConfigurationId("config-1"), startDate: new DateOnly(2025, 7, 1));
        var config2 = Configurations.Build(id: new InvoiceConfigurationId("config-2"), startDate: new DateOnly(2025, 8, 1));
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
        var failing = Configurations.Build(id: new InvoiceConfigurationId("config-failing"), startDate: new DateOnly(2025, 7, 1));
        var healthy = Configurations.Build(id: new InvoiceConfigurationId("config-healthy"), startDate: new DateOnly(2025, 8, 1));
        var recordRepo = new ThrowingInvoiceRecordRepository(
            failing.Id, new InvalidOperationException("Simulated repository failure."));
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
}
