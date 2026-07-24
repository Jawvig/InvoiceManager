using InvoiceManager.AdminWeb.Services;
using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace InvoiceManager.AdminWeb.Pages;

public class AuthorizationModel : PageModel
{
    private readonly IMicrosoftAuthorizationStore authorizationStore;
    private readonly MicrosoftAuthorizationOptions authorizationOptions;

    public AuthorizationModel(
        IMicrosoftAuthorizationStore authorizationStore,
        IOptions<MicrosoftAuthorizationOptions> authorizationOptions)
    {
        this.authorizationStore = authorizationStore;
        this.authorizationOptions = authorizationOptions.Value;
    }

    public bool IsSignedIn { get; private set; }

    public string DisplayName { get; private set; } = "Not signed in";

    public bool IsAuthorizationCaptured { get; private set; }

    public bool CanAuthorize { get; private set; }

    public bool ShowAuthorizeButton => CanAuthorize;

    public string AuthorizeButtonCaption
    {
        get
        {
            return IsAuthorizationCaptured
                ? "Replace workflow authorization"
                : "Capture workflow authorization";
        }
    }

    public string? StatusMessage { get; private set; }

    public IReadOnlyList<string> ConfigurationMessages { get; private set; } = [];

    public async Task OnGetAsync(string? status = null)
    {
        await LoadPageStateAsync(status);
    }

    public IActionResult OnPostAuthorize(bool confirmed)
    {
        if (!confirmed)
        {
            TempData["StatusMessage"] = "Confirm that you intend to replace the unattended workflow account.";
            return RedirectToPage();
        }

        var configurationMessages = GetConfigurationMessages();
        if (configurationMessages.Count > 0)
        {
            TempData["StatusMessage"] = configurationMessages[0];
            return RedirectToPage();
        }

        var redirectUri = Url.Page("/Authorization", null, new { status = "authorized" })
            ?? "/Authorization";
        return Challenge(
            new AuthenticationProperties { RedirectUri = redirectUri },
            MicrosoftOpenIdConnectOptionsSetup.WorkflowAuthorizationScheme);
    }

    public async Task<IActionResult> OnPostResetAsync()
    {
        await authorizationStore.ClearTokenCacheAsync(HttpContext.RequestAborted);
        TempData["StatusMessage"] = "Microsoft authorization was reset.";
        return RedirectToPage();
    }

    public IActionResult OnPostSignOut()
    {
        return SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            CookieAuthenticationDefaults.AuthenticationScheme);
    }

    private async Task LoadPageStateAsync(string? status)
    {
        IsSignedIn = User.Identity?.IsAuthenticated == true;
        DisplayName = IsSignedIn
            ? User.Identity?.Name ?? "Signed in"
            : "Not signed in";
        IsAuthorizationCaptured = await authorizationStore.HasTokenCacheAsync(HttpContext.RequestAborted);

        ConfigurationMessages = GetConfigurationMessages();
        CanAuthorize = ConfigurationMessages.Count == 0;

        StatusMessage = TempData["StatusMessage"] as string;
        if (status == "authorized")
        {
            StatusMessage = "Microsoft authorization was captured.";
        }
    }

    private List<string> GetConfigurationMessages()
    {
        var messages = new List<string>();

        if (!authorizationOptions.HasEntraConfiguration)
        {
            messages.Add("Set MicrosoftAuthorization:TenantId and MicrosoftAuthorization:ClientId before authorizing Microsoft.");
        }

        if (!authorizationOptions.HasClientSecret)
        {
            messages.Add("Set MicrosoftAuthorization:ClientSecret before authorizing Microsoft.");
        }

        if (!authorizationOptions.HasPersistentStore)
        {
            messages.Add("Set MicrosoftAuthorization:KeyVaultUri before captured authorization can be saved.");
        }

        return messages;
    }
}
