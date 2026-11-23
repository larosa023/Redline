using Redline.Core.Fans;
using Redline.Core.Gpu;
using Redline.Hardware.Nvidia;
using Xunit;

namespace Redline.Hardware.Tests.Nvidia;

public class NvidiaTests
{
    static NvidiaDevice Dev(FakeNvml f) => (NvidiaDevice)Assert.Single(new NvidiaBackend(f).Discover());

    [Fact]
    public void NoDll_IsAvailableFalse() => Assert.False(new NvidiaBackend(new FakeNvml { DllPresent = false }).IsAvailable());

    [Fact]
    public void Discover_IdNameDriver()
    {
        var d = Dev(new FakeNvml());
        Assert.Equal("nvidia-GPU-1234", d.Id);
        Assert.Equal("NVIDIA GeForce RTX 4090", d.Name);
        Assert.Equal("581.42", d.DriverVersion);
        Assert.Equal(Vendor.Nvidia, d.Vendor);
    }

    [Fact]
    public void Caps_FromMinMaxQueries()
    {
        var c = Dev(new FakeNvml()).Capabilities;
        Assert.Equal(new ControlRange(34, 133, 1, 100), c.Range(Control.PowerLimit));
        Assert.Equal(new ControlRange(-1000, 1000, 15, 0), c.Range(Control.CoreOffset));
        Assert.Equal(new ControlRange(-2000, 3000, 50, 0), c.Range(Control.MemoryOffset));
        Assert.Equal(new ControlRange(30, 100, 1, 0), c.Range(Control.FanSpeed));
        Assert.False(c.Supports(Control.VoltageOffset));
        Assert.True(c.HasFanCurve);
    }

    [Fact]
    public async Task PowerPercent_ConvertsMilliwatts()
    {
        var f = new FakeNvml();
        var r = await Dev(f).ApplyAsync(TuneSettings.Factory with { PowerLimitPct = 80 }, default);
        Assert.Equal(360_000u, f.LimitMw);
        Assert.Equal(80, r.Actual.PowerLimitPct);
    }

    [Fact]
    public async Task Apply_UsesClockOffsets_WhenAvailable()
    {
        var f = new FakeNvml();
        await Dev(f).ApplyAsync(TuneSettings.Factory with { CoreOffsetMhz = 150 }, default);
        Assert.Contains("SetClockOffsets Graphics 150", f.Calls);
        Assert.DoesNotContain(f.Calls, c => c.StartsWith("SetVfOffset"));
    }

    [Fact]
    public async Task Apply_FallsBackToVfOffset_OnNotSupported()
    {
        var f = new FakeNvml { NewOffsetsApi = false };
        var r = await Dev(f).ApplyAsync(TuneSettings.Factory with { CoreOffsetMhz = 150, MemoryOffsetMhz = 500 }, default);
        Assert.Contains("SetVfOffset Graphics 150", f.Calls);
        Assert.Equal(500, r.Actual.MemoryOffsetMhz);
        Assert.True(r.Matches);
    }

    [Fact]
    public async Task Apply_ReadsBack_ActualReflectsDriver()
    {
        var f = new FakeNvml { DriverCoreCap = 120 };
        var r = await Dev(f).ApplyAsync(TuneSettings.Factory with { CoreOffsetMhz = 150 }, default);
        Assert.Equal(120, r.Actual.CoreOffsetMhz);
        Assert.False(r.Matches);
    }

    [Fact]
    public async Task Apply_NullValues_MeanStock()
    {
        var f = new FakeNvml { CoreOffset = 90, LimitMw = 300_000 };
        var r = await Dev(f).ApplyAsync(TuneSettings.Factory with { MemoryOffsetMhz = 200 }, default);
        Assert.Equal(0, f.CoreOffset);
        Assert.Equal(450_000u, f.LimitMw);
        Assert.Null(r.Actual.CoreOffsetMhz);
        Assert.Null(r.Actual.PowerLimitPct);
        Assert.True(r.Matches);
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4090", true)]
    [InlineData("NVIDIA GeForce RTX 5070 Ti", true)]
    [InlineData("NVIDIA GeForce RTX 3080", false)]
    [InlineData("NVIDIA GeForce RTX 4070 Laptop GPU", false)]
    public void ConnectorTable_16Pin(string name, bool expected) => Assert.Equal(expected, ConnectorTable.Has16Pin(name));

    [Fact]
    public void Rtx4090_Has16Pin() => Assert.True(Dev(new FakeNvml()).Capabilities.Has16PinConnector);

    [Fact]
    public async Task Reset_RestoresDefaultLimitAndZeroOffsetsAndDefaultFan()
    {
        var f = new FakeNvml();
        var d = Dev(f);
        await d.ApplyAsync(new TuneSettings(110, 150, 900, null, FanCurve.Default), default);
        await d.SetFanPercentAsync(70, default);
        await d.ResetAsync(default);
        Assert.Equal(450_000u, f.LimitMw);
        Assert.Equal(0, f.CoreOffset);
        Assert.Equal(0, f.MemOffset);
        Assert.All(f.FanManual, m => Assert.False(m));
        Assert.True((await d.ReadAsync(default)).IsFactory);
    }

    [Fact]
    public async Task FanPercent_SetsAllFans()
    {
        var f = new FakeNvml();
        await Dev(f).SetFanPercentAsync(65, default);
        Assert.Equal([65u, 65u], f.FanPct);
    }

    [Fact]
    public void Telemetry_Reads_AndNullWhenLost()
    {
        var f = new FakeNvml();
        var d = Dev(f);
        var t = d.ReadTelemetry()!;
        Assert.Equal(2730, t.CoreClockMhz);
        Assert.Equal(380, t.PowerW);
        Assert.Equal(61, t.TempC);
        Assert.Equal(6144, t.VramUsedMb);
        f.Lost = true;
        Assert.Null(d.ReadTelemetry());
    }
}
