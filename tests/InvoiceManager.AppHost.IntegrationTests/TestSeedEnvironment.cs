using System.Runtime.CompilerServices;

namespace InvoiceManager.AppHost.IntegrationTests;

/// <summary>
/// The seeder fails fast when the seed file's placeholder tokens have no configured value
/// (InvoiceManager__Seed__DriveId / BillingAccountId). These AppHost integration tests run
/// the real seeder but only assert that the configuration IDs were written, so any non-empty
/// value suffices. Set them for the test process (inherited by the seeder child process)
/// before any test starts, without overriding a developer's real local values.
/// </summary>
internal static class TestSeedEnvironment
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        SetIfMissing("InvoiceManager__Seed__DriveId", "integration-test-drive-id");
        SetIfMissing("InvoiceManager__Seed__DriveName", "integration-test-drive-name");
        SetIfMissing("InvoiceManager__Seed__Microsoft365TestFolderItemId", "integration-test-m365-folder-id");
        SetIfMissing("InvoiceManager__Seed__AzureTestFolderItemId", "integration-test-azure-folder-id");
        SetIfMissing("InvoiceManager__Seed__BillingAccountId", "integration-test-billing-account");
        SetIfMissing("InvoiceManager__Seed__AzureBillingAccountId", "integration-test-azure-billing-account");
    }

    private static void SetIfMissing(string name, string value)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}
