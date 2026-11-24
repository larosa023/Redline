using Redline.Core.Gpu;
using Redline.Hardware.Intel;
using Xunit;

namespace Redline.Hardware.Tests.Intel;

public sealed class FakeIgcl : IIgclApi
{
    public bool Loads = true, OcSupported = true;
    public IgclControl? Freq = new(-200, 250, 1, 0, IgclUnits.MHz);
    public IgclControl? Volt = new(0, 150, 1, 0, IgclUnits.MilliVolts);
    public IgclControl? Power = new(95_000, 228_000, 1_000, 190_000, IgclUnits.MilliWatts);
    public double FreqOffset, VoltOffset, PowerLimit = 190_000;
    public int Waivers, Resets;

    public bool TryLoad() => Loads;
    public IReadOnlyList<IgclGpuInfo> Gpus() => [new(0, "Intel(R) Arc(TM) A770 Graphics", "31.0.101.5590", "pci-0000:03:00.0")];
    public IgclOc Properties(int gpu) => new(OcSupported, Freq, Volt, null, Power);
    public void AcceptWaiver(int gpu) => Waivers++;
    public double GetGpuFrequencyOffset(int gpu) => FreqOffset;
    public void SetGpuFrequencyOffset(int gpu, double v) => FreqOffset = v;
    public double GetGpuVoltageOffset(int gpu) => VoltOffset;
    public void SetGpuVoltageOffset(int gpu, double v) => VoltOffset = v;
    public double GetVramFrequencyOffset(int gpu) => 0;
    public void SetVramFrequencyOffset(int gpu, double v) => throw new IgclException("not supported");
    public double GetPowerLimit(int gpu) => PowerLimit;
    public void SetPowerLimit(int gpu, double v) => PowerLimit = v;
    public void ResetToDefault(int gpu) { Resets++; (FreqOffset, VoltOffset, PowerLimit) = (0, 0, 190_000); }
    public IgclTelemetry? Telemetry(int gpu) => new(2400, 2187, 64, null, 180, 87, 1600, 1050, PowerLimited: true, TempLimited: false, VoltageLimited: false, UtilLimited: false);
}

public class IntelTests
{
    static IntelDevice Device(FakeIgcl f) => (IntelDevice)new IntelBackend(f).Discover().Single();

    [Fact]
    public async Task NoWaiver_ApplyThrows_TelemetryWorks()
    {
        var f = new FakeIgcl();
        var d = Device(f);
        Assert.False(d.WaiverAccepted);
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => d.ApplyAsync(new TuneSettings(null, 50, null, null, null), default));
        Assert.Equal("Intel overclocking waiver not accepted", e.Message);
        Assert.Equal(0, f.FreqOffset);
        var t = d.ReadTelemetry()!;
        Assert.Equal(64, t.TempC);
        Assert.Equal(180, t.PowerW);
        Assert.True(t.Limits.HasFlag(LimitReason.Power));
    }

    [Fact]
    public async Task Waiver_ThenApply_Ok()
    {
        var f = new FakeIgcl();
        var d = Device(f);
        d.AcceptWaiver();
        Assert.Equal(1, f.Waivers);
        var r = await d.ApplyAsync(new TuneSettings(110, 100, null, 25, null), default);
        Assert.Equal((100.0, 25.0, 209_000.0), (f.FreqOffset, f.VoltOffset, f.PowerLimit));
        Assert.True(r.Matches);
        Assert.Equal(new TuneSettings(110, 100, null, 25, null), await d.ReadAsync(default));
    }

    [Fact]
    public void PowerLimitMilliwatts_ToPercent()
    {
        var d = Device(new FakeIgcl());
        Assert.Equal(new ControlRange(50, 120, 1, 100), d.Capabilities.Range(Control.PowerLimit));
        Assert.Equal(new ControlRange(-200, 250, 1, 0), d.Capabilities.Range(Control.CoreOffset));
        Assert.Equal(new ControlRange(0, 150, 1, 0), d.Capabilities.Range(Control.VoltageOffset));
        Assert.False(d.Capabilities.Supports(Control.MemoryOffset));
    }

    [Fact]
    public void PowerLimitWatts_AlsoUnderstood()
    {
        var d = Device(new FakeIgcl { Power = new(95, 228, 1, 190, IgclUnits.Watts), PowerLimit = 190 });
        Assert.Equal(new ControlRange(50, 120, 1, 100), d.Capabilities.Range(Control.PowerLimit));
    }

    [Fact]
    public async Task Reset_WithoutWaiver_DoesNothing_WithWaiver_ResetsDriver()
    {
        var f = new FakeIgcl();
        var d = Device(f);
        await d.ResetAsync(default);
        Assert.Equal(0, f.Resets);
        d.AcceptWaiver();
        await d.ResetAsync(default);
        Assert.Equal(1, f.Resets);
    }

    [Fact]
    public void NoControlLib_IsAvailableFalse() => Assert.False(new IntelBackend(new FakeIgcl { Loads = false }).IsAvailable());

    [Fact]
    public void OcNotSupported_MonitorOnly() => Assert.Empty(Device(new FakeIgcl { OcSupported = false }).Capabilities.Ranges);

    [Fact]
    public void ImplausibleRanges_Dropped()
    {
        var d = Device(new FakeIgcl
        {
            Freq = new(-99999, 99999, 1, 0, IgclUnits.MHz),
            Volt = new(0, 150, 1, 0, IgclUnits.Volts),          // volts, not millivolts: don't guess
            Power = new(95_000, 228_000, 1_000, 0, IgclUnits.MilliWatts),   // no default power: can't make percent
        });
        Assert.Empty(d.Capabilities.Ranges);
    }
}
