using System.Diagnostics;

namespace InvoiceManager.Core;

/// <summary>
/// Shared <see cref="ActivitySource"/> for the invoice workflow. Every span in
/// the service is started from this one source so a single listener (the
/// Functions worker's Application Insights export) captures the whole trace tree:
/// a processing run, the per-record work under it, and each external call
/// (invoice source lookup, OneDrive upload, token acquisition) under that.
/// </summary>
public static class Telemetry
{
    /// <summary>The name registered with the telemetry pipeline so these spans export.</summary>
    public const string ActivitySourceName = "InvoiceManager";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
