namespace InvoiceManager.Core.Integrations.FreeAgent;

/// <summary>
/// A FreeAgent contact selected for bill matching: its resource URL (the only
/// part matching ever keys off) plus a cached display name for showing the
/// selection without a live FreeAgent API call on every page load. The display
/// name is a convenience only - never authoritative, and refreshed from
/// FreeAgent whenever the owning configuration is saved via Edit or Import.
/// </summary>
public sealed record FreeAgentContact(FreeAgentContactIdentity Url, string DisplayName);

/// <summary>
/// Read-only access to FreeAgent's contacts, used by the AdminWeb contact
/// picker to search for a contact and to confirm/refresh a previously-selected
/// one. Deliberately separate from <see cref="IFreeAgentBillMatcher"/> and its
/// siblings - this is a lookup concern, not a bill-matching one.
/// </summary>
public interface IFreeAgentContactDirectory
{
    /// <summary>
    /// Searches for contacts whose name contains <paramref name="query"/>, most
    /// relevant subset returned first. Never throws for "no matches" - returns
    /// an empty list.
    /// </summary>
    Task<IReadOnlyList<FreeAgentContact>> SearchAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a single contact by its resource URL, for confirming an
    /// existing selection still resolves and refreshing its display name.
    /// <see cref="None"/> means specifically that FreeAgent has no contact at
    /// this URL - a genuine functional outcome, not a system error. A system
    /// error (FreeAgent unreachable, rate limited, an unexpected status, the
    /// URL pointing at the wrong environment) throws instead of being folded
    /// into <see cref="None"/>, so a caller can tell "this contact doesn't
    /// exist" apart from "this lookup failed" and show the administrator the
    /// right message for each.
    /// </summary>
    Task<Option<FreeAgentContact>> GetAsync(FreeAgentContactIdentity url, CancellationToken cancellationToken = default);
}
