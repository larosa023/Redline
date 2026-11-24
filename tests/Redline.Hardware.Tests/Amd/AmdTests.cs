using Redline.Core.Gpu;
using Redline.Hardware.Amd;
using Xunit;

namespace Redline.Hardware.Tests.Amd;

public sealed class FakeAdlx : IAdlxApi
{
    public bool Loads = true, Gfx = true, Vram = true, Power = true, Gfx2 = true;
    public AdlxRange MaxFreqRange = new(500, 3200, 1), VoltRange = new(700, 1150, 5), VramRange = new(2400, 2700, 4), PowerRange = new(-10, 15, 1);
    public int MaxFreq = 2900, Volt = 1100, VramMax = 2500, PowerPct;
    public int FactoryMaxFreq = 2900, FactoryVolt = 1100, FactoryVram = 2500;
    public bool AtFactory = true;
    public int Resets;
    public List<string> Log = new();

    public bool TryLoad() => Loads;
    public IReadOnlyList<AdlxGpuInfo> Gpus() => [new(0, "AMD Radeon RX 7900 XTX", "1234", 24560, "24.10.1")];
    public AdlxSupport Support(int gpu) => new(Gfx, Gfx2, Vram, Power);
    public AdlxRanges Ranges(int gpu) => new(Gfx2 ? MaxFreqRange : null, Gfx2 ? VoltRange : null, Vram ? VramRange : null, Power ? PowerRange : null);
    public bool IsAtFactory(int gpu) => AtFactory;
    public AdlxValues Read(int gpu) => new(MaxFreq, Volt, VramMax, PowerPct);
    public void Write(int gpu, AdlxValues v)
    {
        Log.Add($"write {v}");
        if (v.GpuMaxFreqMhz is { } f) MaxFreq = f;
        if (v.GpuVoltageMv is { } mv) Volt = mv;
        if (v.VramMaxFreqMhz is { } m) VramMax = m;
        if (v.PowerPct is { } p) PowerPct = p;
        AtFactory = MaxFreq == FactoryMaxFreq && Volt == FactoryVolt && VramMax == FactoryVram && PowerPct == 0;
    }
    public void ResetToFactory(int gpu)
    {
        Resets++; Log.Add("reset");
        (MaxFreq, Volt, VramMax, PowerPct, AtFactory) = (FactoryMaxFreq, FactoryVolt, FactoryVram, 0, true);
    }
    public AdlxMetrics? Metrics(int gpu) => new(55, 2650, 2500, 61, 78, 310, 1450, 6200, 1050, 355);
}

public class AmdTests
{
    static AmdDevice Device(FakeAdlx a) => (AmdDevice)new AmdBackend(a).Discover().Single();

    [Fact]
    public void Gfx2_MapsCoreOffsetVoltageMemoryAndPower()
    {
        var d = Device(new FakeAdlx());
        var c = d.Capabilities;
        Assert.Equal(new ControlRange(-500, 300, 1, 0), c.Range(Control.CoreOffset));      // 3200 max − 2900 factory; floor −500
        Assert.Equal(new ControlRange(-400, 50, 5, 0), c.Range(Control.VoltageOffset));    // 700..1150 around 1100
        Assert.Equal(new ControlRange(-100, 200, 4, 0), c.Range(Control.MemoryOffset));
        Assert.Equal(new ControlRange(90, 115, 1, 100), c.Range(Control.PowerLimit));
        Assert.False(c.Supports(Control.FanSpeed));
        Assert.Equal(Vendor.Amd, d.Vendor);
        Assert.StartsWith("amd-", d.Id);
    }

    [Fact]
    public async Task Apply_WritesAbsoluteValuesFromFactory_AndReadsBackOffsets()
    {
        var a = new FakeAdlx();
        var d = Device(a);
        var r = await d.ApplyAsync(new TuneSettings(110, 120, 100, -50, null), default);
        Assert.Equal((3020, 1050, 2600, 10), (a.MaxFreq, a.Volt, a.VramMax, a.PowerPct));
        Assert.True(r.Matches);
        Assert.Equal(new TuneSettings(110, 120, 100, -50, null), await d.ReadAsync(default));
    }

    [Fact]
    public async Task Factory_ReadsAsFactory_AndResetCallsDriverReset()
    {
        var a = new FakeAdlx();
        var d = Device(a);
        Assert.True((await d.ReadAsync(default)).IsFactory);
        await d.ApplyAsync(new TuneSettings(null, 60, null, null, null), default);
        await d.ResetAsync(default);
        Assert.Equal(1, a.Resets);
        Assert.True((await d.ReadAsync(default)).IsFactory);
    }

    [Fact]
    public async Task TunedElsewhere_FirstApplyResetsToFactoryBeforeWriting()
    {
        // Adrenalin already raised the clock: Redline can't know the factory value until it resets once.
        var a = new FakeAdlx { MaxFreq = 3000, AtFactory = false };
        var d = Device(a);
        await d.ApplyAsync(new TuneSettings(null, 50, null, null, null), default);
        Assert.Equal("reset", a.Log[0]);
        Assert.Equal(2950, a.MaxFreq);
    }

    [Fact]
    public void NotSupported_ControlsAbsent_TelemetryWorks()
    {
        var d = Device(new FakeAdlx { Gfx = false, Gfx2 = false, Vram = false, Power = false });
        Assert.Empty(d.Capabilities.Ranges);
        var t = d.ReadTelemetry()!;
        Assert.Equal(61, t.TempC);
        Assert.Equal(78, t.HotspotC);
        Assert.Equal(355, t.PowerW);      // total board power preferred over chip power
        Assert.Equal(1450, t.FanRpm);
        Assert.Equal(24560, t.VramTotalMb);
    }

    [Fact]
    public void Gfx1Only_CoreOffsetAbsent()
    {
        var d = Device(new FakeAdlx { Gfx2 = false });
        Assert.False(d.Capabilities.Supports(Control.CoreOffset));
        Assert.False(d.Capabilities.Supports(Control.VoltageOffset));
        Assert.True(d.Capabilities.Supports(Control.PowerLimit));
    }

    [Theory]
    [InlineData(0, 0)]          // empty range
    [InlineData(500, 99999)]    // nonsense maximum (e.g. wrong struct layout)
    [InlineData(3000, 3200)]    // factory value outside the range
    public void ImplausibleFrequencyRange_ControlDropped(int min, int max)
    {
        var d = Device(new FakeAdlx { MaxFreqRange = new(min, max, 1) });
        Assert.False(d.Capabilities.Supports(Control.CoreOffset));
    }

    [Fact]
    public void ImplausiblePowerRange_Dropped() =>
        Assert.False(Device(new FakeAdlx { PowerRange = new(-90, 400, 1) }).Capabilities.Supports(Control.PowerLimit));

    [Fact]
    public void NoAdlx_NotAvailable() => Assert.False(new AmdBackend(new FakeAdlx { Loads = false }).IsAvailable());
}
