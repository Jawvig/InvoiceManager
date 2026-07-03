using InvoiceManager.Core.Repositories;
using InvoiceManager.TestSupport;

namespace InvoiceManager.Core.Tests;

public sealed class ConfigurationSeederTests
{
    [Fact]
    public async Task SeedAsync_InsertsConfiguration_WhenNotPresent()
    {
        var repository = new InMemoryConfigurationRepository();
        var seeder = new ConfigurationSeeder(repository);
        var config = Configurations.Build(new InvoiceConfigurationId("m365-business-basic"));

        await seeder.SeedAsync([config]);

        Assert.True(repository.Contains(new InvoiceConfigurationId("m365-business-basic")));
    }

    [Fact]
    public async Task SeedAsync_DoesNotOverwriteExistingConfiguration()
    {
        var original = Configurations.Build(
            new InvoiceConfigurationId("m365-business-basic"),
            invoiceDescription: "Original");
        var repository = new InMemoryConfigurationRepository(original);
        var seeder = new ConfigurationSeeder(repository);
        var modified = original with { InvoiceDescription = "Modified" };

        await seeder.SeedAsync([modified]);

        Assert.Equal("Original", repository.Get(new InvoiceConfigurationId("m365-business-basic")).InvoiceDescription);
    }

    [Fact]
    public async Task SeedAsync_InsertsNewAndSkipsExisting_WhenBothProvided()
    {
        var existing = Configurations.Build(
            new InvoiceConfigurationId("existing"),
            invoiceDescription: "Original");
        var repository = new InMemoryConfigurationRepository(existing);
        var seeder = new ConfigurationSeeder(repository);
        var modifiedExisting = existing with { InvoiceDescription = "Modified" };
        var newConfig = Configurations.Build(new InvoiceConfigurationId("new-config"));

        await seeder.SeedAsync([modifiedExisting, newConfig]);

        Assert.Equal("Original", repository.Get(new InvoiceConfigurationId("existing")).InvoiceDescription);
        Assert.True(repository.Contains(new InvoiceConfigurationId("new-config")));
    }

    private sealed class InMemoryConfigurationRepository : IInvoiceConfigurationRepository
    {
        private readonly Dictionary<InvoiceConfigurationId, InvoiceConfiguration> store;

        public InMemoryConfigurationRepository(params InvoiceConfiguration[] initial)
        {
            store = initial.ToDictionary(c => c.Id);
        }

        public bool Contains(InvoiceConfigurationId id) => store.ContainsKey(id);

        public InvoiceConfiguration Get(InvoiceConfigurationId id) => store[id];

        public Task<IReadOnlyList<InvoiceConfiguration>> ListActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InvoiceConfiguration>>(
                store.Values.Where(c => c.IsActive).ToList());

        public Task CreateIfNotExistsAsync(InvoiceConfiguration configuration, CancellationToken cancellationToken = default)
        {
            store.TryAdd(configuration.Id, configuration);
            return Task.CompletedTask;
        }
    }
}
