using System.Text;
using System.Text.RegularExpressions;
using NodaMoney;

namespace InvoiceManager.Core;

public static partial class InvoiceConfigurationValidation
{
    public static IReadOnlyList<string> Validate(InvoiceConfiguration configuration)
    {
        var errors = new List<string>();

        if (!IdPattern().IsMatch(configuration.Id.Value))
            errors.Add("Invoice configuration ID must be lowercase kebab-case.");

        if (!Enum.IsDefined(configuration.IntegrationType))
            errors.Add("Integration type is not supported.");
        if (!Enum.IsDefined(configuration.Frequency))
            errors.Add("Frequency is not supported.");
        if (!Enum.IsDefined(configuration.DefaultVatMode))
            errors.Add("VAT mode is not supported.");

        if (configuration.AmountMatchingCriteria is AmountMatchingCriteria amount)
        {
            if (amount.Amount.Amount <= 0)
                errors.Add("Expected amount must be greater than zero.");
            if (amount.AmountTolerance < 0)
                errors.Add("Amount tolerance must be non-negative.");
            try
            {
                _ = Currency.FromCode(amount.Amount.Currency.Code);
            }
            catch (ArgumentException)
            {
                errors.Add("Currency must be a recognized ISO 4217 currency code.");
            }
        }

        if (configuration.DateToleranceDays is < 0 or > 365)
            errors.Add("Date tolerance must be between 0 and 365 days.");
        if (string.IsNullOrWhiteSpace(configuration.BillingAccountId))
            errors.Add("Billing account is required.");
        if (string.IsNullOrWhiteSpace(configuration.OneDriveDestination.DisplayPath))
            errors.Add("OneDrive destination is required.");

        return errors;
    }

    public static string GenerateSlug(string? invoiceDescription, IntegrationType integrationType)
    {
        var source = string.IsNullOrWhiteSpace(invoiceDescription)
            ? $"{integrationType.ToString().ToLowerInvariant()} invoice"
            : invoiceDescription;
        var normalized = source.Normalize(NormalizationForm.FormD);
        var slug = SlugSeparators().Replace(
            new string(normalized.Where(c => char.GetUnicodeCategory(c) !=
                System.Globalization.UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant(),
            "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "invoice" : slug;
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugSeparators();
}
