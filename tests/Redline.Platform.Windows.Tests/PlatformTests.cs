using Redline.Core.Cpu;
using Redline.Platform.Windows;
using Xunit;

namespace Redline.Platform.Windows.Tests;

public class PlatformTests
{
    [Fact]
    public void PowerPlan_ReadsCurrentBoostMode()
    {
        var s = new PowrProfPowerPlan().ReadCurrent();
        Assert.NotNull(s.BoostMode);
        Assert.InRange(s.MaxProcessorStatePct ?? -1, 5, 100);
    }

    [Fact]
    public void PowerPlan_WriteThenRestore_RoundTrips()
    {
        if (!Environment.IsPrivilegedProcess) return; // needs admin; CI runners are admin
        var plan = new PowrProfPowerPlan();
        var original = plan.ReadCurrent();
        try
        {
            plan.Apply(new CpuBoostSettings(CpuBoostMode.Disabled, 99));
            var now = plan.ReadCurrent();
            Assert.Equal(CpuBoostMode.Disabled, now.BoostMode);
            Assert.Equal(99, now.MaxProcessorStatePct);
        }
        finally { plan.Apply(original); }
        Assert.Equal(original, plan.ReadCurrent());
    }

    [Theory]
    [InlineData("Display", 4101, true)]
    [InlineData("nvlddmkm", 153, true)]
    [InlineData("nvlddmkm", 13, true)]
    [InlineData("amdkmdag", 4101, true)]
    [InlineData("Service Control Manager", 7036, false)]
    [InlineData("Display", 1, false)]
    public void EventLog_RecognizesDriverResets(string provider, int id, bool expected) =>
        Assert.Equal(expected, StabilityEventFilter.IsDriverReset(provider, id));

    [Fact]
    public async Task LhmCpuMonitor_ReturnsName()
    {
        using var m = new LhmCpuMonitor();
        await m.Opened.WaitAsync(TimeSpan.FromSeconds(60));
        var s = m.Read();
        Assert.NotNull(s);
        Assert.False(string.IsNullOrWhiteSpace(s!.Name));
    }

    [Fact]
    public void PawnIoDetector_DoesNotThrow() => _ = PawnIoDetector.IsInstalled();
}

public class PerCoreSensorTests
{
    [Fact]
    public void OnlyOneClockPerCore_EffectiveClocksIgnored()
    {
        var sensors = new (string Name, int Index, float? Value)[]
        {
            ("Core #1", 0, 5500), ("Core #1 (Effective)", 1, 800), ("Core #2", 2, 5400), ("Core #2 (Effective)", 3, 1600),
            ("Cores (Average Effective)", 4, 1200), ("Bus Speed", 5, 100),
        };
        Assert.Equal([5500.0, 5400.0], Redline.Platform.Windows.LhmCpuMonitor.PerCore(sensors, "Core #"));
    }

    [Fact]
    public void PerCoreLoads_IgnoreThreads()
    {
        var sensors = new (string Name, int Index, float? Value)[]
        {
            ("CPU Total", 0, 9), ("CPU Core #1", 1, 20), ("CPU Core #1 Thread #1", 2, 30), ("CPU Core #2", 3, 4),
        };
        Assert.Equal([20.0, 4.0], Redline.Platform.Windows.LhmCpuMonitor.PerCore(sensors, "CPU Core #"));
    }
}
