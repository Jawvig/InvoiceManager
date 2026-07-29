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
        var duplicate = Configurations.Build(
            isActive: false,
            integrationConfiguration: new GraphEmailIntegrationConfiguration("sender@example.com", "Invoice")) with
        {
            Id = existing.Id,
        };

        await Assert.ThrowsAsync<DuplicateInvoiceConfigurationException>(() =>
            service.CreateAsync(duplicate, Actor));
    }

    [Fact]
    public async Task Create_RejectsSameSearchCriteria_EvenWithDifferentFolderAndId()
    {
        var existing = Configurations.Build(
            id: new("existing-config"),
            integrationConfiguration: new MicrosoftBillingIntegrationConfiguration("account-1"));
        var service = new InvoiceConfigurationService(new FakeConfigurationRepository(existing));
        var duplicate = Configurations.Build(
            id: new("new-config"),
            isActive: false,
            integrationConfiguration: new MicrosoftBillingIntegrationConfiguration("account-1"),
            oneDriveFolder: new OneDriveFolder("drive-2", "Drive Two", "folder-2", "/Bills/Other"));

        await Assert.ThrowsAsync<DuplicateInvoiceConfigurationException>(() =>
            service.CreateAsync(duplicate, Actor));
    }

    [Fact]
    public async Task Update_RejectsSameSearchCriteriaAsAnotherConfiguration()
    {
        var other = Configurations.Build(
            id: new("other-config"),
            integrationConfiguration: new GraphEmailIntegrationConfiguration("sender@example.com", "Invoice \\d+"));
        var original = Configurations.Build(
            id: new("editing-config"),
            isActive: false,
            integrationConfiguration: new GraphEmailIntegrationConfiguration("sender@example.com", "Different \\d+"));
        var repository = new FakeConfigurationRepository(other, original);
        var service = new InvoiceConfigurationService(repository);
        var updated = original with
        {
            IntegrationConfiguration = new GraphEmailIntegrationConfiguration("sender@example.com", "Invoice \\d+"),
        };

        await Assert.ThrowsAsync<DuplicateInvoiceConfigurationException>(() =>
            service.UpdateAsync(original, updated, "etag-editing-config", Actor));
    }

    [Fact]
    public async Task Restore_KeepsCurrentIdentityIntegrationAndActivationState()
    {
        var current = Configurations.Build(isActive: true);
        var historical = current with
        {
            Id = new("different-id"),
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
