using InvoiceManager.Infrastructure.MicrosoftAuthorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace InvoiceManager.AdminWeb.Pages;

public class IndexModel : PageModel
{
    private readonly IMicrosoftAuthorizationStore authorizationStore;
    private readonly MicrosoftAuthorizationOptions authorizationOptions;

    public IndexModel(
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

    public bool ShowAuthorizeButton => CanAuthorize && (!IsSignedIn || !IsAuthorizationCaptured);

    public string AuthorizeButtonCaption
    {
        get
        {
            if (IsSignedIn)
            {
                return "Authorize";
            }

            return IsAuthorizationCaptured ? "Sign in" : "Sign in and authorize";
        }
    }

    public string? StatusMessage { get; private set; }

    public IReadOnlyList<string> ConfigurationMessages { get; private set; } = [];

    public async Task OnGetAsync(string? status = null)
    {
        await LoadPageStateAsync(status);
    }

    public IActionResult OnPostAuthorize()
    {
        var configurationMessages = GetConfigurationMessages();
        if (configurationMessages.Count > 0)
        {
            TempData["StatusMessage"] = configurationMessages[0];
            return RedirectToPage();
        }

        var redirectUri = Url.Page("/Index", null, new { status = "authorized" })
            ?? "/";
        return Challenge(
            new AuthenticationProperties { RedirectUri = redirectUri },
            OpenIdConnectDefaults.AuthenticationScheme);
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
