namespace InvoiceManager.TestSupport;

/// <summary>
/// A <see cref="TimeProvider"/> for tests that records every requested delay and
/// completes it immediately, so code under test that awaits <c>TimeProvider.Delay</c>
/// (for example throttling back-off) never really sleeps. The callback is queued
/// rather than invoked inline so it runs after <c>Delay</c> has finished wiring up
/// its timer.
/// </summary>
public sealed class RecordingTimeProvider : TimeProvider
{
    private readonly List<TimeSpan> delays = [];

    public IReadOnlyList<TimeSpan> Delays => delays;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        delays.Add(dueTime);
        return new ImmediateTimer(callback, state);
    }

    private sealed class ImmediateTimer : ITimer
    {
        public ImmediateTimer(TimerCallback callback, object? state) =>
            ThreadPool.QueueUserWorkItem(_ => callback(state));

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
