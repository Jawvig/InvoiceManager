namespace InvoiceManager.TestSupport;

/// <summary>
/// A <see cref="TimeProvider"/> that always reports a fixed instant, for
/// deterministic "today" in tests.
/// </summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public FixedTimeProvider(DateOnly today)
        : this(new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero))
    {
    }

    public override DateTimeOffset GetUtcNow() => now;
}
