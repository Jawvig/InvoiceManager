using System.Runtime.CompilerServices;

namespace InvoiceManager.AdminWeb.PlaywrightTests;

/// <summary>
/// The seeder fails fast when the seed file's placeholder tokens have no configured value
/// (InvoiceManager__Seed__DriveId / BillingAccountId), and AdminWeb waits for the seeder to
/// complete before starting. The billing-account values are set here with a synthetic fallback
/// when a developer's real local values aren't present, since nothing in this suite calls Graph
/// with them directly. The OneDrive folder values are different: Create/Edit now verify every
/// submitted folder selection against Microsoft Graph (see
/// MicrosoftResourceDiscovery.GetFolderAsync and TestOneDriveFolder), so a synthetic placeholder
/// here wouldn't produce a clear setup error — it would let the AppHost start, and only fail much
/// later with a confusing remote Graph error once a test actually submits the form. Require them
/// to already be real values instead.
/// </summary>
internal static class TestSeedEnvironment
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        RequireSet("InvoiceManager__Seed__DriveId");
        RequireSet("InvoiceManager__Seed__DriveName");
        RequireSet("InvoiceManager__Seed__Microsoft365TestFolderItemId");
        RequireSet("InvoiceManager__Seed__AzureTestFolderItemId");
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

    private static void RequireSet(string name)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)))
        {
            throw new InvalidOperationException(
                $"{name} must be set to a real, Graph-verifiable value before running this test " +
                "project. Create/Edit now verify every submitted OneDrive folder selection against " +
                "Microsoft Graph, so a synthetic placeholder would only surface as a confusing " +
                "remote Graph failure partway through a test, not a clear setup error. Run " +
                "tools/dev-setup/Set-SeedEnvironment.ps1 (or set it manually) and restart your " +
                "shell/IDE so this test process and the AppHost both inherit it.");
        }
    }
}
