using InvoiceManager.Core;
using InvoiceManager.TestSupport;

namespace InvoiceManager.Core.Tests;

public sealed class InvoiceConfigurationServiceTests
{
    private static readonly InvoiceConfigurationActor Actor = new("actor-id", "Admin User");

    [Fact]
    public async Task Create_RequiresInactiveDraft()
    {
        var service = new InvoiceConfigurationService(new FakeConfigurationRepository());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(Configurations.Build(isActive: true), Actor));
    }

    [Fact]
    public async Task Create_RejectsDuplicateIdAcrossIntegrationTypes()
    {
        var existing = Configurations.Build();
        var service = new InvoiceConfigurationService(new FakeConfigurationRepository(existing));
        var duplicate = Configurations.Build(isActive: false) with
        {
            Id = existing.Id,
            IntegrationType = IntegrationType.Azure,
        };

        await Assert.ThrowsAsync<DuplicateInvoiceConfigurationException>(() =>
            service.CreateAsync(duplicate, Actor));
    }

    [Fact]
    public async Task Restore_KeepsCurrentIdentityIntegrationAndActivationState()
    {
        var current = Configurations.Build(isActive: true);
        var historical = current with
        {
            Id = new("different-id"),
            IntegrationType = IntegrationType.Azure,
            InvoiceDescription = "Historical description",
            IsActive = false,
        };
        var repository = new FakeConfigurationRepository(current);
        var service = new InvoiceConfigurationService(repository);
        var revision = new InvoiceConfigurationRevision(
            "revision-1", current.Id, current.IntegrationType,
            InvoiceConfigurationRevisionAction.Updated, DateTimeOffset.UtcNow,
            "old-actor", "Old actor", historical);

        var restored = await service.RestoreAsync(
            new(current, "etag"), revision, Actor);

        Assert.Equal(current.Id, restored.Configuration.Id);
        Assert.Equal(current.IntegrationType, restored.Configuration.IntegrationType);
        Assert.True(restored.Configuration.IsActive);
        Assert.Equal("Historical description", restored.Configuration.InvoiceDescription);
    }
}
