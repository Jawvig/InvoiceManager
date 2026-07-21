using System.Runtime.CompilerServices;

namespace InvoiceManager.AdminWeb.PlaywrightTests;

/// <summary>
/// The seeder fails fast when the seed file's placeholder tokens have no configured value
/// (InvoiceManager__Seed__DriveId / BillingAccountId), and AdminWeb waits for the seeder to
/// complete before starting. Set them for the test process (inherited by the seeder child
/// process) before any test starts, without overriding a developer's real local values.
/// </summary>
internal static class TestSeedEnvironment
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        SetIfMissing("InvoiceManager__Seed__DriveId", "integration-test-drive-id");
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
