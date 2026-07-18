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

        public Task<IReadOnlyList<StoredInvoiceConfiguration>> ListAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoredInvoiceConfiguration>>(
                store.Values.Select(c => new StoredInvoiceConfiguration(c, "etag")).ToList());

        public Task<Option<StoredInvoiceConfiguration>> GetAsync(
            InvoiceConfigurationId id,
            IntegrationType integrationType,
            CancellationToken cancellationToken = default)
        {
            Option<StoredInvoiceConfiguration> result = store.TryGetValue(id, out var configuration)
                ? new StoredInvoiceConfiguration(configuration, "etag")
                : Option.None;
            return Task.FromResult(result);
        }

        public Task CreateIfNotExistsAsync(InvoiceConfiguration configuration, CancellationToken cancellationToken = default)
        {
            store.TryAdd(configuration.Id, configuration);
            return Task.CompletedTask;
        }

        public Task<StoredInvoiceConfiguration> CreateAsync(
            InvoiceConfiguration configuration,
            InvoiceConfigurationActor actor,
            CancellationToken cancellationToken = default)
        {
            store.Add(configuration.Id, configuration);
            return Task.FromResult(new StoredInvoiceConfiguration(configuration, "etag"));
        }

        public Task<StoredInvoiceConfiguration> ReplaceAsync(
            InvoiceConfiguration configuration,
            string etag,
            InvoiceConfigurationRevisionAction action,
            InvoiceConfigurationActor actor,
            CancellationToken cancellationToken = default)
        {
            store[configuration.Id] = configuration;
            return Task.FromResult(new StoredInvoiceConfiguration(configuration, "etag-next"));
        }

        public Task<IReadOnlyList<InvoiceConfigurationRevision>> ListRevisionsAsync(
            InvoiceConfigurationId id,
            IntegrationType integrationType,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InvoiceConfigurationRevision>>([]);
    }
}
