using System.ComponentModel.DataAnnotations;

namespace InvoiceManager.AdminWeb.Services;

public sealed class AdminAuthorizationOptions
{
    public const string SectionName = "AdminAuthorization";

    [Required]
    public string GroupObjectId { get; set; } = "";
}
