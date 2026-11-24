using Redline.Core.AutoTune;
using Redline.Core.Gpu;
using Redline.Core.Safety;
using Redline.Core.Sim;
using Redline.Core.Tests.Fakes;
using Xunit;

namespace Redline.Core.Tests.AutoTune;

public class PerformanceTests : IDisposable
{
    readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();
    static readonly AutoTuneOptions Perf = new(AutoTuneMode.Performance);

    (AutoTuner tuner, SimGpuDevice dev, SimStressRunner stress, SafeStartJournal journal) Make(SimGpuSpec spec)
    {
        var dev = new SimGpuDevice(spec);
        var stress = new SimStressRunner();
        var journal = new SafeStartJournal(_dir.File("journal.json"));
        return (new AutoTuner(dev, stress, journal, SafetyOptions.Default), dev, stress, journal);
    }

    [Fact]
    public async Task FindsStableCore_MinusMargin()
    {
        var (t, dev, _, _) = Make(TestGpus.Spec(coreMax: 500, stableCore: 180, memMax: 2000, stableMem: 1100));
        var r = await t.RunAsync(Perf, null, default);
        Assert.Equal(AutoTuneOutcome.Completed, r.Outcome);
        Assert.Equal(150, r.Best.CoreOffsetMhz);
        Assert.Equal(r.Best, dev.Current);
    }

    [Fact]
    public async Task Memory_StopsWhereBandwidthPeaks_NotAtTheCeiling()
    {
        // GDDR6X corrects its own errors: past its best point it gets slower instead of failing.
        var spec = TestGpus.Spec(coreMax: 500, stableCore: 180, memMax: 6000, stableMem: 5000) with { MemBandwidthPeakMhz = 1500 };
        var (t, _, stress, _) = Make(spec);
        var r = await t.RunAsync(Perf, null, default);
        Assert.Equal(AutoTuneOutcome.Completed, r.Outcome);
        Assert.Equal(1450, r.Best.MemoryOffsetMhz);                                   // best point, one step of margin
        Assert.True(stress.Tested.Max(x => x.MemoryOffsetMhz ?? 0) <= 1550);          // stopped right after the drop
    }

    [Fact]
    public async Task MemoryOnly_KeepsTheCurrentCore_AndTestsOnlyMemory()
    {
        var (t, dev, stress, _) = Make(TestGpus.Spec(coreMax: 500, stableCore: 180, memMax: 2000, stableMem: 1100));
        var r = await t.RunAsync(Perf with { KeepFrom = new TuneSettings(null, 150, 2300, null, null) }, null, default);
        Assert.Equal(AutoTuneOutcome.Completed, r.Outcome);
        Assert.Equal(150, r.Best.CoreOffsetMhz);
        Assert.Equal(1050, r.Best.MemoryOffsetMhz);
        Assert.All(stress.Tested.Skip(1), x => Assert.Equal(150, x.CoreOffsetMhz));   // after the stock baseline, core never moves
        Assert.Equal(r.Best, dev.Current);
    }

    [Fact]
    public async Task FindsStableMemory_MinusMargin()
    {
        var (t, _, _, _) = Make(TestGpus.Spec(coreMax: 500, stableCore: 180, memMax: 2000, stableMem: 1100));
        var r = await t.RunAsync(Perf, null, default);
        Assert.Equal(1050, r.Best.MemoryOffsetMhz);
    }

    [Fact]
    public async Task NeverRaisesPowerLimit_ByDefault()
    {
        var (t, _, stress, _) = Make(TestGpus.Spec());
        var r = await t.RunAsync(Perf, null, default);
        Assert.Null(r.Best.PowerLimitPct);
        Assert.All(stress.Tested, s => Assert.True(s.PowerLimitPct is null or <= 100));
    }

    [Fact]
    public async Task StaysInSafeRange()
    {
        var (t, _, stress, _) = Make(TestGpus.Spec(coreMax: 300, stableCore: 1000));
        var r = await t.RunAsync(Perf, null, default);
        Assert.Equal(150, r.Best.CoreOffsetMhz);
        Assert.All(stress.Tested, s => Assert.True((s.CoreOffsetMhz ?? 0) <= 180));
    }

    [Fact]
    public async Task Mismatch_StopsWalk_NotCountedAsPass()
    {
        var (t, _, _, _) = Make(TestGpus.Spec(stableCore: 1000, clamp: new() { [Control.CoreOffset] = 90 }));
        var r = await t.RunAsync(Perf, null, default);
        Assert.Equal(60, r.Best.CoreOffsetMhz);
    }

    [Fact]
    public async Task BaselineFails_ReturnsFailedAtBaseline_AndResets()
    {
        var (t, dev, stress, _) = Make(TestGpus.Spec());
        stress.ForceFail = (i, _) => i == 0;
        var r = await t.RunAsync(Perf, null, default);
        Assert.Equal(AutoTuneOutcome.FailedAtBaseline, r.Outcome);
        Assert.True(r.Best.IsFactory);
        Assert.True(dev.Current.IsFactory);
    }

    [Fact]
    public async Task Cancel_AppliesLastGood()
    {
        var (t, dev, stress, _) = Make(TestGpus.Spec(stableCore: 1000));
        using var cts = new CancellationTokenSource();
        stress.OnRun = i => { if (i == 4) cts.Cancel(); }; // baseline, +15, +30, +45 passed; cancel during +60
        var r = await t.RunAsync(Perf, null, cts.Token);
        Assert.Equal(AutoTuneOutcome.Cancelled, r.Outcome);
        Assert.Equal(15, r.Best.CoreOffsetMhz); // 45 − 2×15
        Assert.Equal(r.Best, dev.Current);
    }

    [Fact]
    public async Task FinalFails_BacksOffThenSucceeds()
    {
        var (t, _, stress, _) = Make(TestGpus.Spec(stableCore: 180, stableMem: 1100));
        var finals = 0;
        stress.ForceFail = (_, d) => d == TimeSpan.FromMinutes(5) && finals++ == 0;
        var r = await t.RunAsync(Perf, null, default);
        Assert.Equal(AutoTuneOutcome.Completed, r.Outcome);
        Assert.Equal(135, r.Best.CoreOffsetMhz);
        Assert.Equal(1000, r.Best.MemoryOffsetMhz);
    }

    [Fact]
    public async Task FinalFailsRepeatedly_EndsAtFactory()
    {
        var (t, dev, stress, _) = Make(TestGpus.Spec());
        stress.ForceFail = (_, d) => d == TimeSpan.FromMinutes(5);
        var r = await t.RunAsync(Perf, null, default);
        Assert.True(r.Best.IsFactory);
        Assert.True(dev.Current.IsFactory);
    }

    [Fact]
    public async Task JournalPendingWrittenBeforeEachApply()
    {
        var journalPath = _dir.File("journal.json");
        var inner = new SimGpuDevice(TestGpus.Spec());
        var spy = new JournalCheckingDevice(inner, new SafeStartJournal(journalPath));
        var t = new AutoTuner(spy, new SimStressRunner(), new SafeStartJournal(journalPath), SafetyOptions.Default);
        await t.RunAsync(Perf, null, default);
        Assert.True(spy.Applies > 3);
        Assert.Equal(0, spy.Violations);
        Assert.All(new SafeStartJournal(journalPath).Inspect().Devices, s => Assert.Null(s.Pending));
    }

    [Fact]
    public async Task ReportsProgress()
    {
        var (t, _, _, _) = Make(TestGpus.Spec());
        var seen = new List<AutoTuneProgress>();
        await t.RunAsync(Perf, new SyncProgress<AutoTuneProgress>(seen.Add), default);
        Assert.NotEmpty(seen);
        Assert.Equal(1.0, seen[^1].Fraction, 3);
    }

    sealed class SyncProgress<T>(Action<T> a) : IProgress<T> { public void Report(T v) => a(v); }

    sealed class JournalCheckingDevice(SimGpuDevice inner, SafeStartJournal journal) : IGpuDevice, IWrapsSim
    {
        public int Applies, Violations;
        public string Id => inner.Id; public string Name => inner.Name; public Vendor Vendor => inner.Vendor;
        public string DriverVersion => inner.DriverVersion; public Capabilities Capabilities => inner.Capabilities;
        public Task<TuneSettings> ReadAsync(CancellationToken ct) => inner.ReadAsync(ct);
        public Task<ApplyResult> ApplyAsync(TuneSettings s, CancellationToken ct)
        {
            Applies++;
            var dev = journal.Inspect().Devices.FirstOrDefault(x => x.DeviceId == Id);
            if (!s.IsFactory && dev?.Pending != s) Violations++;
            return inner.ApplyAsync(s, ct);
        }
        public Task ResetAsync(CancellationToken ct) => inner.ResetAsync(ct);
        public Telemetry? ReadTelemetry() => inner.ReadTelemetry();
        public SimGpuDevice Inner => inner;
    }
}
