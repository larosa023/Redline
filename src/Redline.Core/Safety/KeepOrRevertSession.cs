using Redline.Core.Gpu;

namespace Redline.Core.Safety;

public enum KeepOrRevertOutcome { Kept, Reverted }

/// <summary>"Keep these settings?" countdown. Without confirmation within the timeout (default 15 s) the outcome is Reverted.</summary>
public sealed class KeepOrRevertSession : IDisposable
{
    public static TimeSpan DefaultTimeout { get; } = TimeSpan.FromSeconds(15);

    readonly TimeProvider _time;
    readonly DateTimeOffset _deadline;
    readonly ITimer _timer;
    readonly Action<KeepOrRevertOutcome>? _onDone;
    readonly TaskCompletionSource<KeepOrRevertOutcome> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public KeepOrRevertSession(TuneSettings previous, TuneSettings applied, TimeProvider time, TimeSpan? timeout = null,
        Action<KeepOrRevertOutcome>? onDone = null)
    {
        _onDone = onDone;
        Previous = previous;
        Applied = applied;
        _time = time;
        var t = timeout ?? DefaultTimeout;
        _deadline = time.GetUtcNow() + t;
        _timer = time.CreateTimer(_ => Finish(KeepOrRevertOutcome.Reverted), null, t, Timeout.InfiniteTimeSpan);
    }

    public Guid Token { get; } = Guid.NewGuid();
    public TuneSettings Previous { get; }
    public TuneSettings Applied { get; }
    public Task<KeepOrRevertOutcome> Completion => _tcs.Task;

    public TimeSpan Remaining
    {
        get
        {
            if (_tcs.Task.IsCompleted) return TimeSpan.Zero;
            var r = _deadline - _time.GetUtcNow();
            return r < TimeSpan.Zero ? TimeSpan.Zero : r;
        }
    }

    public void Confirm() => Finish(KeepOrRevertOutcome.Kept);

    void Finish(KeepOrRevertOutcome o)
    {
        if (!_tcs.TrySetResult(o)) return;
        _timer.Dispose();
        _onDone?.Invoke(o);
    }

    public void RevertNow() => Finish(KeepOrRevertOutcome.Reverted);

    public void Dispose() => _timer.Dispose();
}
