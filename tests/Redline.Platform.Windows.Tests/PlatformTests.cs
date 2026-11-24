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
