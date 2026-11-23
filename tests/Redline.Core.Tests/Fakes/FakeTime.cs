namespace Redline.Core.Tests.Fakes;

/// <summary>Manually advanced clock with timer support (stand-in for Microsoft.Extensions.TimeProvider.Testing).</summary>
public sealed class FakeTime(DateTimeOffset? start = null) : TimeProvider
{
    DateTimeOffset _now = start ?? new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
    readonly List<FakeTimer> _timers = new();
    readonly object _gate = new();

    public override DateTimeOffset GetUtcNow() { lock (_gate) return _now; }
    public override long GetTimestamp() { lock (_gate) return _now.UtcTicks; }
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public void Advance(TimeSpan by)
    {
        var target = GetUtcNow() + by;
        while (true)
        {
            FakeTimer? next;
            lock (_gate)
            {
                next = _timers.Where(t => t.Due is { } d && d <= target).OrderBy(t => t.Due).FirstOrDefault();
                if (next is null) { _now = target; break; }
                _now = next.Due!.Value;
                next.Due = next.Period > TimeSpan.Zero && next.Period != Timeout.InfiniteTimeSpan ? _now + next.Period : null;
            }
            next.Callback(next.State);
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var t = new FakeTimer(this, callback, state);
        t.Change(dueTime, period);
        lock (_gate) _timers.Add(t);
        return t;
    }

    sealed class FakeTimer(FakeTime owner, TimerCallback cb, object? state) : ITimer
    {
        public TimerCallback Callback => cb;
        public object? State => state;
        public DateTimeOffset? Due;
        public TimeSpan Period;
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
                Period = period;
            }
            return true;
        }
        public void Dispose() { lock (owner._gate) owner._timers.Remove(this); }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
