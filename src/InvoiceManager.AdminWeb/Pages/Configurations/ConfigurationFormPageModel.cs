using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using InvoiceManager.Core;
using InvoiceManager.Core.Integrations.FreeAgent;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InvoiceManager.AdminWeb.Pages.Configurations;

public abstract class ConfigurationFormPageModel(
    IMicrosoftResourceDiscovery discovery, IFreeAgentContactDirectory contactDirectory) : PageModel
{
    protected IMicrosoftResourceDiscovery Discovery { get; } = discovery;
    protected IFreeAgentContactDirectory ContactDirectory { get; } = contactDirectory;
    public abstract ConfigurationFormInput Input { get; set; }
    public abstract bool IsEdit { get; }
    public IReadOnlyList<BillingAccountChoice> BillingAccounts { get; protected set; } = [];
    public IReadOnlyList<string> DiscoveryWarnings => discoveryWarnings;
    private readonly List<string> discoveryWarnings = [];
    public bool ShowMissingBillingWarning => IsEdit && Input.IntegrationType != IntegrationType.GraphEmail &&
        !BillingAccounts.Any(x => x.Id == Input.OriginalBillingAccountId);

    /// <summary>
    /// True only on the Import review step. Gates the "confirm this is correct" checkboxes in
    /// _ConfigurationForm.cshtml: an imported OneDrive folder or billing account ID can still
    /// resolve successfully against Graph/discovery in the target environment (e.g. dev and prod
    /// sharing a tenant) while being the *wrong* destination for it, so re-verifying existence
    /// alone isn't enough — the user must actively confirm each one before it can be saved.
    /// </summary>
    public virtual bool RequiresImportConfirmation => false;

    [BindProperty]
    public bool ConfirmedFolderSelection { get; set; }

    [BindProperty]
    public bool ConfirmedBillingAccountSelection { get; set; }

    /// <summary>Whether the current caller is authorized to mutate configurations. Also gates the
    /// AJAX discovery handlers below so an unauthenticated/unauthorized request is rejected rather
    /// than silently returning an empty list.</summary>
    protected abstract Task<bool> CanMutateAsync();

    protected async Task LoadDiscoveryAsync(CancellationToken cancellationToken, bool required = true)
    {
        if (Input.IntegrationType != IntegrationType.GraphEmail)
        {
            try
            {
                BillingAccounts = await Discovery.ListBillingAccountsAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var message = $"Billing-account discovery failed: {ex.Message}";
                if (required) ModelState.AddModelError("Input.BillingAccountId", message);
                else discoveryWarnings.Add(message + " An unchanged stored account may be preserved.");
            }
        }
    }

    /// <summary>Lazily loaded by <c>configuration-wizard.js</c> once a Microsoft billing
    /// integration is selected (Create) or unconditionally in the background (Edit), so the page
    /// no longer blocks its initial render on billing-account discovery.</summary>
    public async Task<IActionResult> OnGetBillingAccountsAsync(CancellationToken cancellationToken)
    {
        if (!await CanMutateAsync()) return Unauthorized();
        try
        {
            var accounts = await Discovery.ListBillingAccountsAsync(cancellationToken);
            return new JsonResult(accounts);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new JsonResult(new { error = ex.Message }) { StatusCode = StatusCodes.Status502BadGateway };
        }
    }

    /// <summary>Backs the OneDrive folder picker: lists all drives belonging to the workflow
    /// account so the user can choose which one to browse.</summary>
    public async Task<IActionResult> OnGetOneDriveDrivesAsync(CancellationToken cancellationToken)
    {
        if (!await CanMutateAsync()) return Unauthorized();
        try
        {
            var drives = await Discovery.ListDrivesAsync(cancellationToken);
            return new JsonResult(drives);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new JsonResult(new { error = ex.Message }) { StatusCode = StatusCodes.Status502BadGateway };
        }
    }

    /// <summary>Backs the OneDrive folder picker's drill-down: lists a single level of folder
    /// children (drive root when <paramref name="folderItemId"/> is empty). The client repeats
    /// this call as the user navigates rather than the server walking the whole tree.</summary>
    public async Task<IActionResult> OnGetOneDriveFolderChildrenAsync(
        string driveId, string? folderItemId, CancellationToken cancellationToken)
    {
        if (!await CanMutateAsync()) return Unauthorized();
        if (string.IsNullOrWhiteSpace(driveId)) return BadRequest();
        try
        {
            var folders = await Discovery.ListFolderChildrenAsync(driveId, folderItemId, cancellationToken);
            return new JsonResult(folders);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new JsonResult(new { error = ex.Message }) { StatusCode = StatusCodes.Status502BadGateway };
        }
    }

    /// <summary>Backs the FreeAgent contact picker's search box.</summary>
    public async Task<IActionResult> OnGetFreeAgentContactsAsync(string query, CancellationToken cancellationToken)
    {
        if (!await CanMutateAsync()) return Unauthorized();
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 3) return BadRequest();
        try
        {
            var contacts = await ContactDirectory.SearchAsync(query.Trim(), cancellationToken);
            return new JsonResult(contacts.Select(c => new { url = c.Url.Url.OriginalString, c.DisplayName }));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new JsonResult(new { error = ex.Message }) { StatusCode = StatusCodes.Status502BadGateway };
        }
    }

    /// <summary>
    /// Confirms the posted FreeAgent contact URL still resolves against FreeAgent and refreshes
    /// <see cref="ConfigurationFormInput.FreeAgentContactDisplayName"/> from the live result -
    /// covers a contact renamed since it was last picked, a contact deleted between picker search
    /// and submission, a contact URL that (for Import) never went through the picker in this
    /// environment at all, and a forged request that sets the hidden URL/display-name fields
    /// directly without using the picker - the same trust-boundary concern
    /// <see cref="ResolveFolderAsync"/> documents for the OneDrive folder fields. Called from every
    /// save path's OnPostAsync (Create, Edit, Import) before <see cref="ConfigurationFormInput.Build"/>,
    /// only when <see cref="ConfigurationFormInput.HasFreeAgentMatching"/> is set. Adds a model
    /// error and returns false when the URL is malformed, doesn't resolve, or the lookup itself
    /// failed - the three are reported with distinct messages (see below) rather than one generic
    /// "could not be confirmed", since only the first two are the administrator's problem to fix
    /// and the third is a system error that needs its own diagnostic detail, not to be mistaken
    /// for the contact having been deleted.
    /// </summary>
    protected async Task<bool> RefreshFreeAgentContactAsync(CancellationToken cancellationToken)
    {
        if (!Input.HasFreeAgentMatching) return true;

        FreeAgentContactIdentity contactUrl;
        try
        {
            contactUrl = new FreeAgentContactIdentity(Input.FreeAgentContactUrl?.Trim() ?? "");
        }
        catch (ArgumentException)
        {
            ModelState.AddModelError(string.Empty, "FreeAgent contact URL must be an absolute https URI.");
            return false;
        }

        Option<FreeAgentContact> result;
        try
        {
            result = await ContactDirectory.GetAsync(contactUrl, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ModelState.AddModelError(string.Empty, $"Could not confirm the FreeAgent contact: {ex.Message}");
            return false;
        }

        if (result is FreeAgentContact contact)
        {
            Input.FreeAgentContactDisplayName = contact.DisplayName;
            return true;
        }

        ModelState.AddModelError(string.Empty, "The FreeAgent contact could not be found - it may have been deleted.");
        return false;
    }

    /// <summary>
    /// Resolves the posted OneDrive folder fields into a trusted <see cref="OneDriveFolder"/>.
    /// The posted fields are hidden inputs populated by the picker, but nothing stops a forged
    /// request from setting them directly, so they must not be trusted outright. When they
    /// exactly match <paramref name="storedFolder"/> (Edit, unchanged), the already-validated
    /// stored value is reused without another Graph call; otherwise the selection is verified
    /// against Graph, which also supplies the authoritative <c>DriveName</c>/<c>FolderPath</c>
    /// rather than trusting the posted display strings. Returns <c>null</c> when the posted
    /// item doesn't resolve to a real, currently-existing folder.
    /// </summary>
    protected async Task<OneDriveFolder?> ResolveFolderAsync(
        OneDriveFolder? storedFolder, CancellationToken cancellationToken)
    {
        if (storedFolder is not null &&
            Input.DriveId == storedFolder.DriveId &&
            Input.DriveName == storedFolder.DriveName &&
            Input.FolderItemId == storedFolder.FolderItemId &&
            Input.FolderPath == storedFolder.FolderPath)
        {
            return storedFolder;
        }

        if (string.IsNullOrWhiteSpace(Input.DriveId) || string.IsNullOrWhiteSpace(Input.FolderItemId))
            return null;

        return await Discovery.GetFolderAsync(Input.DriveId, Input.FolderItemId, cancellationToken);
    }
}
