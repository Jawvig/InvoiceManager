using System.Security.Claims;
using InvoiceManager.Core;

namespace InvoiceManager.AdminWeb.Services;

public static class AdminClaims
{
    public static InvoiceConfigurationActor ToConfigurationActor(this ClaimsPrincipal user) =>
        new(
            user.FindFirstValue("oid") ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("The signed-in administrator has no stable object ID claim."),
            user.Identity?.Name ?? user.FindFirstValue("name") ?? "Administrator");
}
