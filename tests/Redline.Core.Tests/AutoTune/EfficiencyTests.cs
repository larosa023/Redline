using Redline.Core.AutoTune;
using Redline.Core.Safety;
using Redline.Core.Sim;
using Redline.Core.Tests.Fakes;
using Xunit;

namespace Redline.Core.Tests.AutoTune;

public class EfficiencyTests : IDisposable
{
    readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();
    static readonly AutoTuneOptions Eff = new(AutoTuneMode.Efficiency);

    (AutoTuner, SimGpuDevice, SimStressRunner) Make(SimGpuSpec spec)
    {
        var dev = new SimGpuDevice(spec);
        var stress = new SimStressRunner();
        return (new AutoTuner(dev, stress, new SafeStartJournal(_dir.File("j.json")), SafetyOptions.Default), dev, stress);
    }

    [Fact]
    public async Task LowersPowerWhileWithin2Percent()
    {
        var (t, dev, _) = Make(TestGpus.Spec());
        var r = await t.RunAsync(Eff, null, default);
        Assert.Equal(AutoTuneOutcome.Completed, r.Outcome);
        Assert.Equal(85, r.Best.PowerLimitPct); // 80% is the lowest within 2% of stock; +5% margin
        Assert.True(r.FinalScore >= r.BaselineScore * 0.98);
        Assert.Equal(r.Best, dev.Current);
    }

    [Fact]
    public async Task NeverIncreasesAnything()
    {
        var (t, _, stress) = Make(TestGpus.Spec(voltage: true));
        await t.RunAsync(Eff, null, default);
        Assert.All(stress.Tested, s =>
        {
            Assert.True(s.PowerLimitPct is null or <= 100);
            Assert.True(s.CoreOffsetMhz is null or <= 0);
            Assert.True(s.MemoryOffsetMhz is null or <= 0);
            Assert.True(s.VoltageOffsetMv is null or <= 0);
        });
    }

    [Fact]
    public async Task VoltageWalk_WhenSupported_KeepsMargin()
    {
        var (t, _, _) = Make(TestGpus.Spec(voltage: true, minStableVoltageMv: -60));
        var r = await t.RunAsync(Eff, null, default);
        Assert.Equal(-50, r.Best.VoltageOffsetMv);
    }

    [Fact]
    public async Task VoltageWalk_OnlyWhenSupported()
    {
        var (t, _, stress) = Make(TestGpus.Spec(voltage: false));
        var r = await t.RunAsync(Eff, null, default);
        Assert.Null(r.Best.VoltageOffsetMv);
        Assert.All(stress.Tested, s => Assert.Null(s.VoltageOffsetMv));
    }

    [Fact]
    public async Task Result_ReportsPowerSaved()
    {
        var (t, _, _) = Make(TestGpus.Spec());
        var r = await t.RunAsync(Eff, null, default);
        Assert.True(r.FinalPowerW < r.BaselinePowerW);
        Assert.Equal(340, r.FinalPowerW, 1);
    }
}
