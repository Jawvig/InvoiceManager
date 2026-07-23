namespace InvoiceManager.AdminWeb.PlaywrightTests;

/// <summary>
/// A real, Graph-verifiable OneDrive drive/folder for tests that submit the configuration form:
/// Create now verifies every submitted folder selection against Microsoft Graph (see
/// ConfigurationFormPageModel.ResolveFolderAsync), so a fabricated drive/item ID like
/// "drive-test"/"folder-test" is rejected with a Graph 400/404 rather than accepted. Reuses the
/// same <c>InvoiceManager__Seed__*</c> environment variables the seeder itself resolves via
/// <c>tools/dev-setup/Set-SeedEnvironment.ps1</c>, so these values are only real (and these tests
/// only pass) in an environment where that script has been run — matching how this whole
/// integration project already depends on real Microsoft Graph credentials and is excluded from CI.
/// </summary>
internal static class TestOneDriveFolder
{
    public static string DriveId =>
        Environment.GetEnvironmentVariable("InvoiceManager__Seed__DriveId") ?? "";

    public static string DriveName =>
        Environment.GetEnvironmentVariable("InvoiceManager__Seed__DriveName") ?? "";

    // The isolated Test/Bills/Microsoft 365 folder, not the production Bills/Microsoft 365 one.
    public static string FolderItemId =>
        Environment.GetEnvironmentVariable("InvoiceManager__Seed__Microsoft365TestFolderItemId") ?? "";
}
