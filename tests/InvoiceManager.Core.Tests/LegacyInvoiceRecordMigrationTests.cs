using InvoiceManager.Core;
using InvoiceManager.TestSupport;

namespace InvoiceManager.Core.Tests;

public sealed class LegacyInvoiceRecordMigrationTests
{
    [Fact]
    public async Task Run_ReportsActionableFailure_WhenConfigurationIsMissing()
    {
        var record = Records.Build() with { ProcessingSnapshot = null };
        var migration = new LegacyInvoiceRecordMigration(
            new InMemoryInvoiceRecordRepository(record),
            new FakeConfigurationRepository());

        var result = await migration.RunAsync();

        var failure = Assert.Single(result.Failures);
        Assert.Equal(record.ConfigurationId, failure.ConfigurationId);
        Assert.Contains("not found", failure.Message);
    }

    [Fact]
    public async Task Run_MigratesOnlyRetryableLegacyRecords_AndIsIdempotent()
    {
        var configuration = Configurations.Build();
        var expected = Records.Build(configuration) with { ProcessingSnapshot = null };
        var error = Records.Build(
            configuration,
            expectedDate: configuration.StartDate.AddMonths(1),
            state: new RetrievalError("x")) with
        { ProcessingSnapshot = null };
        var completed = Records.Build(
            configuration,
            expectedDate: configuration.StartDate.AddMonths(2),
            state: new NotFound()) with
        { ProcessingSnapshot = null };
        var repository = new InMemoryInvoiceRecordRepository(expected, error, completed);
        var migration = new LegacyInvoiceRecordMigration(repository, new FakeConfigurationRepository(configuration));

        var first = await migration.RunAsync();
        var second = await migration.RunAsync();

        Assert.Equal(2, first.Migrated);
        Assert.Equal(0, first.Failed);
        Assert.Equal(0, second.Migrated);
        Assert.Equal(2, second.Skipped);
        Assert.Null(repository.All.Single(x => x.State is NotFound).ProcessingSnapshot);
        Assert.All(repository.All.Where(x => x.State is Expected or RetrievalError),
            record => Assert.NotNull(record.ProcessingSnapshot));
    }
}
