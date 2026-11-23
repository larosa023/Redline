using Redline.Core.Gpu;
using Redline.Core.Sim;
using Redline.Core.Tests.Fakes;
using Xunit;

namespace Redline.Core.Tests.Gpu;

public class SimGpuDeviceTests
{
    [Fact]
    public void Factory_IsAllNull()
    {
        var f = TuneSettings.Factory;
        Assert.Null(f.PowerLimitPct); Assert.Null(f.CoreOffsetMhz); Assert.Null(f.MemoryOffsetMhz);
        Assert.Null(f.VoltageOffsetMv); Assert.Null(f.Fan);
        Assert.True(f.IsFactory);
        Assert.False((f with { CoreOffsetMhz = 15 }).IsFactory);
    }

    [Fact]
    public async Task Apply_WithinRange_ActualEqualsRequested()
    {
        var d = new SimGpuDevice(TestGpus.Spec());
        var req = new TuneSettings(90, 150, 1000, null, null);
        var r = await d.ApplyAsync(req, default);
        Assert.Equal(req, r.Actual);
        Assert.True(r.Matches);
        Assert.Equal(req, await d.ReadAsync(default));
    }

    [Fact]
    public async Task Apply_DriverClampsValue_ActualReported()
    {
        var d = new SimGpuDevice(TestGpus.Spec(clamp: new() { [Control.CoreOffset] = 120 }));
        var r = await d.ApplyAsync(TuneSettings.Factory with { CoreOffsetMhz = 150 }, default);
        Assert.Equal(120, r.Actual.CoreOffsetMhz);
        Assert.False(r.Matches);
    }

    [Fact]
    public async Task Apply_UnsupportedControl_Ignored()
    {
        var d = new SimGpuDevice(TestGpus.Spec(voltage: false));
        var r = await d.ApplyAsync(TuneSettings.Factory with { VoltageOffsetMv = -50 }, default);
        Assert.Null(r.Actual.VoltageOffsetMv);
    }

    [Fact]
    public async Task Reset_ReturnsToFactory()
    {
        var d = new SimGpuDevice(TestGpus.Spec());
        await d.ApplyAsync(new TuneSettings(90, 150, 1000, null, null), default);
        await d.ResetAsync(default);
        Assert.True((await d.ReadAsync(default)).IsFactory);
    }

    [Fact]
    public void Telemetry_Null_WhenSimDeviceLost()
    {
        var d = new SimGpuDevice(TestGpus.Spec());
        Assert.NotNull(d.ReadTelemetry());
        d.SimulateLoss(true);
        Assert.Null(d.ReadTelemetry());
        d.SimulateLoss(false);
        Assert.NotNull(d.ReadTelemetry());
    }

    [Fact]
    public void Settings_GetAndWith_ByControl()
    {
        var s = TuneSettings.Factory.With(Control.CoreOffset, 45).With(Control.PowerLimit, 80);
        Assert.Equal(45, s.Get(Control.CoreOffset));
        Assert.Equal(80, s.PowerLimitPct);
        Assert.Null(s.Get(Control.MemoryOffset));
    }
}
