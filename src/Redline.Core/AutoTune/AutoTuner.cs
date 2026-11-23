using Redline.Core.Gpu;
using Redline.Core.Safety;

namespace Redline.Core.AutoTune;

/// <summary>
/// Finds a stable tune step by step. Every step is journaled before it is applied, every result keeps a safety
/// margin, and the power limit is never raised unless the user allowed it.
/// </summary>
public sealed class AutoTuner(IGpuDevice device, IStressRunner stress, SafeStartJournal journal, SafetyOptions safety)
{
    const int MaxFinalRetries = 2;

    sealed class Run(AutoTuneOptions o, IProgress<AutoTuneProgress>? progress)
    {
        public readonly AutoTuneOptions O = o;
        public readonly IProgress<AutoTuneProgress>? Progress = progress;
        public readonly List<AutoTuneStep> Log = new();
        public TuneSettings LastGood = TuneSettings.Factory;
        public int Done, PlannedSteps = 1;
        public (Control Control, int Step, int MarginSteps)? Walking;
    }

    public async Task<AutoTuneResult> RunAsync(AutoTuneOptions o, IProgress<AutoTuneProgress>? progress, CancellationToken ct)
    {
        var run = new Run(o, progress);
        var caps = device.Capabilities;
        run.PlannedSteps = EstimateSteps(o, caps);
        StressResult baseline;
        try
        {
            baseline = await TestAsync(run, "Baseline at stock", TuneSettings.Factory, o.Step, ct);
        }
        catch (OperationCanceledException) { return await CancelAsync(run, null); }

        if (!baseline.Stable)
        {
            await ApplyAsync(TuneSettings.Factory, "baseline failed", CancellationToken.None, confirm: true);
            return new AutoTuneResult(AutoTuneOutcome.FailedAtBaseline, TuneSettings.Factory, baseline.Score, baseline.Score,
                baseline.AvgPowerW, baseline.AvgPowerW, run.Log);
        }

        try
        {
            var candidate = o.Mode == AutoTuneMode.Performance
                ? await PerformanceAsync(run, caps, ct)
                : await EfficiencyAsync(run, caps, baseline, ct);
            return await FinalizeAsync(run, candidate, baseline, ct);
        }
        catch (OperationCanceledException) { return await CancelAsync(run, baseline); }
    }

    async Task<WalkState> PerformanceAsync(Run run, Capabilities caps, CancellationToken ct)
    {
        var o = run.O;
        var state = new WalkState(TuneSettings.Factory);
        if (o.AllowPowerAbove100 && caps.Range(Control.PowerLimit) is { } pr)
        {
            var opts = safety with { AllowPowerAbove100 = true };
            var p = SafetyPolicy.Validate(TuneSettings.Factory with { PowerLimitPct = pr.Max }, caps, opts).Clamped.PowerLimitPct;
            if (p is > 100)
            {
                var s = TuneSettings.Factory with { PowerLimitPct = p };
                var r = await TestAsync(run, $"Power limit {p}%", s, o.Step, ct);
                if (r.Stable) state = state with { Settings = s };
            }
        }
        state = await WalkUpAsync(run, state, caps, Control.CoreOffset, o.CoreStepMhz, o.CoreMarginSteps, "Core", ct);
        state = await WalkUpAsync(run, state, caps, Control.MemoryOffset, o.MemStepMhz, o.MemMarginSteps, "Memory", ct);
        return state;
    }

    /// <summary>Lower power, then voltage, while staying stable and within the performance tolerance of stock. Never raises anything.</summary>
    async Task<WalkState> EfficiencyAsync(Run run, Capabilities caps, StressResult baseline, CancellationToken ct)
    {
        var o = run.O;
        var minScore = baseline.Score * (1 - o.EfficiencyTolerance);
        var st = new WalkState(TuneSettings.Factory);
        st = await WalkDownAsync(run, st, caps, Control.PowerLimit, 100, o.PowerStepPct, minScore, v => $"Power limit {v}%", ct);
        st = await WalkDownAsync(run, st, caps, Control.VoltageOffset, 0, o.VoltageStepMv, minScore, v => $"Voltage {v} mV", ct);
        return st;
    }

    async Task<WalkState> WalkDownAsync(Run run, WalkState st, Capabilities caps, Control c, int start, int step, double minScore,
        Func<int, string> label, CancellationToken ct)
    {
        if (caps.Range(c) is not { } driver) return st;
        step = Math.Max(step, driver.Step);
        int? lastPass = null;
        run.Walking = (c, -step, 1);
        for (var v = start - step; v >= driver.Min; v -= step)
        {
            var s = st.Settings.With(c, v);
            var r = await TestAsync(run, label(v), s, run.O.Step, ct);
            if (!r.Stable || r.Score < minScore) break;
            lastPass = v;
        }
        run.Walking = null;
        if (lastPass is null) return st;
        var result = Math.Min(start, lastPass.Value + step);      // one step back toward stock as safety margin
        var settings = st.Settings.With(c, result == start ? null : result);
        run.LastGood = settings;
        return new WalkState(settings) { Steps = new Dictionary<Control, int>(st.Steps) { [c] = step } };
    }

    sealed record WalkState(TuneSettings Settings)
    {
        public Dictionary<Control, int> Steps { get; init; } = new();
    }

    async Task<WalkState> WalkUpAsync(Run run, WalkState st, Capabilities caps, Control c, int step, int marginSteps, string label, CancellationToken ct)
    {
        if (caps.Range(c) is not { } driver) return st;
        var safe = SafetyPolicy.SafeRange(c, driver, advanced: false);
        step = Math.Max(step, driver.Step);
        var lastPass = 0;
        run.Walking = (c, step, marginSteps);
        for (var v = step; v <= safe.Max; v += step)
        {
            var s = st.Settings.With(c, v);
            var r = await TestAsync(run, $"{label} +{v} MHz", s, run.O.Step, ct);
            if (!r.Stable) break;
            lastPass = v;
        }
        run.Walking = null;
        var result = Math.Max(0, lastPass - marginSteps * step);
        var settings = st.Settings.With(c, result == 0 ? null : result);
        run.LastGood = settings;
        var steps = new Dictionary<Control, int>(st.Steps) { [c] = step };
        return new WalkState(settings) { Steps = steps };
    }

    async Task<AutoTuneResult> FinalizeAsync(Run run, WalkState st, StressResult baseline, CancellationToken ct)
    {
        var candidate = st.Settings;
        for (var attempt = 0; attempt <= MaxFinalRetries; attempt++)
        {
            if (candidate.IsFactory) break;
            var r = await TestAsync(run, attempt == 0 ? "Final check" : $"Final check (retry {attempt})", candidate, run.O.Final, ct);
            if (r.Stable)
            {
                run.Progress?.Report(new AutoTuneProgress(1.0, "Done", candidate, TimeSpan.Zero));
                return new AutoTuneResult(AutoTuneOutcome.Completed, candidate, baseline.Score, r.Score, baseline.AvgPowerW, r.AvgPowerW, run.Log);
            }
            candidate = BackOff(candidate, st.Steps);
        }
        await ApplyAsync(TuneSettings.Factory, "final check failed", CancellationToken.None, confirm: true);
        run.Progress?.Report(new AutoTuneProgress(1.0, "Done", TuneSettings.Factory, TimeSpan.Zero));
        return new AutoTuneResult(AutoTuneOutcome.Completed, TuneSettings.Factory, baseline.Score, baseline.Score,
            baseline.AvgPowerW, baseline.AvgPowerW, run.Log);
    }

    /// <summary>One more step toward stock on every walked control.</summary>
    static TuneSettings BackOff(TuneSettings s, IReadOnlyDictionary<Control, int> steps)
    {
        foreach (var (c, step) in steps)
        {
            if (s.Get(c) is not { } v) continue;
            var nv = c switch
            {
                Control.PowerLimit => Math.Min(100, v + step),       // efficiency: power back up toward 100
                Control.VoltageOffset => Math.Min(0, v + step),      // efficiency: voltage back toward 0
                _ => Math.Max(0, v - step),                          // offsets back toward 0
            };
            s = s.With(c, nv == 0 || (c == Control.PowerLimit && nv == 100) ? null : nv);
        }
        return s;
    }

    async Task<AutoTuneResult> CancelAsync(Run run, StressResult? baseline)
    {
        var best = run.LastGood;
        if (run.Walking is { } w && best.Get(w.Control) is { } v)
        {
            var nv = v - w.MarginSteps * w.Step;                          // toward stock: offsets down, power/voltage up
            var atStock = w.Control switch { Control.PowerLimit => nv >= 100, Control.VoltageOffset => nv >= 0, _ => nv <= 0 };
            best = best.With(w.Control, atStock ? null : nv);
        }
        await ApplyAsync(best, "auto-tune cancelled", CancellationToken.None, confirm: true);
        var b = baseline;
        return new AutoTuneResult(AutoTuneOutcome.Cancelled, best, b?.Score ?? 0, b?.Score ?? 0, b?.AvgPowerW ?? 0, b?.AvgPowerW ?? 0, run.Log);
    }

    async Task<StressResult> TestAsync(Run run, string label, TuneSettings s, TimeSpan duration, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var remainingSteps = Math.Max(0, run.PlannedSteps - run.Done);
        run.Progress?.Report(new AutoTuneProgress(Math.Min(0.99, run.Done / (double)run.PlannedSteps), label, s,
            TimeSpan.FromTicks(run.O.Step.Ticks * remainingSteps)));
        var applied = await ApplyAsync(s, label, ct);
        StressResult r;
        try { r = await stress.RunAsync(device, duration, ct); }
        catch (OperationCanceledException) { throw; }
        if (!applied.Matches) r = r with { Completed = false };   // the driver didn't take it: never count as a pass
        var passed = r.Stable;
        run.Log.Add(new AutoTuneStep(label, s, r, passed));
        run.Done++;
        if (passed)
        {
            journal.MarkConfirmed(device.Id, applied.Actual);
            run.LastGood = s;
        }
        else
        {
            await ApplyAsync(run.LastGood, "back to last good", CancellationToken.None, confirm: true);
        }
        return r;
    }

    async Task<ApplyResult> ApplyAsync(TuneSettings s, string reason, CancellationToken ct, bool confirm = false)
    {
        journal.MarkPending(device.Id, s, reason);
        var r = await device.ApplyAsync(s, ct);
        if (confirm) journal.MarkConfirmed(device.Id, r.Actual);
        return r;
    }

    static int EstimateSteps(AutoTuneOptions o, Capabilities caps)
    {
        var n = 2; // baseline + final
        foreach (var (c, step) in new[] { (Control.CoreOffset, o.CoreStepMhz), (Control.MemoryOffset, o.MemStepMhz) })
            if (caps.Range(c) is { } d)
                n += Math.Max(1, SafetyPolicy.SafeRange(c, d, false).Max / Math.Max(step, d.Step) / 2);
        return n;
    }
}
