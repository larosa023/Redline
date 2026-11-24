using Redline.Core.Gpu;

namespace Redline.Hardware.Intel;

public sealed class IntelBackend(IIgclApi api) : IGpuBackend
{
    public Vendor Vendor => Vendor.Intel;

    public bool IsAvailable()
    {
        try { return api.TryLoad() && api.Gpus().Count > 0; }
        catch (Exception e) when (e is IgclException or DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    public IReadOnlyList<IGpuDevice> Discover() => api.Gpus().Select(g => (IGpuDevice)new IntelDevice(api, g)).ToList();
}

/// <summary>
/// Intel Arc via IGCL. Intel requires accepting its overclocking waiver before any change; until then the card is
/// monitor-only. Ranges that look wrong (or use units Redline doesn't expect) are dropped instead of guessed.
/// </summary>
public sealed class IntelDevice : IGpuDevice, IRequiresWaiver
{
    readonly IIgclApi _api;
    readonly int _gpu;
    readonly double _defaultPower;
    readonly IgclUnits _powerUnits;

    public IntelDevice(IIgclApi api, IgclGpuInfo info)
    {
        _api = api; _gpu = info.Index;
        Name = info.Name; DriverVersion = info.DriverVersion; Id = "intel-" + info.Id;
        var ranges = new Dictionary<Control, ControlRange>();
        IgclOc? oc = null;
        try { oc = api.Properties(_gpu); } catch (IgclException) { }
        if (oc is { Supported: true })
        {
            if (Offset(oc.GpuFrequencyOffset, IgclUnits.MHz, -1000, 1000) is { } core) ranges[Control.CoreOffset] = core;
            if (Offset(oc.GpuVoltageOffset, IgclUnits.MilliVolts, -300, 300) is { } volt) ranges[Control.VoltageOffset] = volt;
            if (Offset(oc.VramFrequencyOffset, IgclUnits.MHz, -1000, 2000) is { } mem) ranges[Control.MemoryOffset] = mem;
            if (oc.PowerLimit is { } p && Watts(p.Default, p.Units) is { } def and >= 10 and <= 1000 && p.Max > p.Min)
            {
                var min = (int)Math.Ceiling(p.Min / p.Default * 100);
                var max = (int)Math.Floor(p.Max / p.Default * 100);
                if (min is >= 10 and <= 100 && max is >= 100 and <= 200) { ranges[Control.PowerLimit] = new(min, max, 1, 100); _defaultPower = p.Default; _powerUnits = p.Units; }
            }
        }
        Capabilities = new Capabilities(ranges, HasFanCurve: false, Has16PinConnector: false);
    }

    static ControlRange? Offset(IgclControl? c, IgclUnits units, int lo, int hi)
    {
        if (c is null || c.Units != units || c.Max <= c.Min || c.Min < lo || c.Max > hi || c.Min > 0 || c.Max < 0) return null;
        return new((int)Math.Ceiling(c.Min), (int)Math.Floor(c.Max), Math.Max(1, (int)Math.Round(c.Step)), 0);
    }

    static double? Watts(double v, IgclUnits u) => u switch { IgclUnits.Watts => v, IgclUnits.MilliWatts => v / 1000, _ => null };

    public string Id { get; }
    public string Name { get; }
    public Vendor Vendor => Vendor.Intel;
    public string DriverVersion { get; }
    public Capabilities Capabilities { get; }
    public bool WaiverAccepted { get; private set; }

    public void AcceptWaiver()
    {
        _api.AcceptWaiver(_gpu);
        WaiverAccepted = true;
    }

    bool Has(Control c) => Capabilities.Supports(c);
    static int? NonZero(double v) => (int)Math.Round(v) is var i && i != 0 ? i : null;

    public Task<TuneSettings> ReadAsync(CancellationToken ct)
    {
        int? power = null;
        if (Has(Control.PowerLimit) && _defaultPower > 0)
        {
            var p = (int)Math.Round(_api.GetPowerLimit(_gpu) / _defaultPower * 100);
            power = p == 100 ? null : p;
        }
        return Task.FromResult(new TuneSettings(power,
            Has(Control.CoreOffset) ? NonZero(_api.GetGpuFrequencyOffset(_gpu)) : null,
            Has(Control.MemoryOffset) ? NonZero(_api.GetVramFrequencyOffset(_gpu)) : null,
            Has(Control.VoltageOffset) ? NonZero(_api.GetGpuVoltageOffset(_gpu)) : null,
            null));
    }

    public async Task<ApplyResult> ApplyAsync(TuneSettings s, CancellationToken ct)
    {
        if (!WaiverAccepted) throw new InvalidOperationException("Intel overclocking waiver not accepted");
        int Clamp(Control c, int? v, int dflt) => Capabilities.Range(c)!.Clamp(v ?? dflt);
        if (Has(Control.PowerLimit)) _api.SetPowerLimit(_gpu, Math.Round(_defaultPower * Clamp(Control.PowerLimit, s.PowerLimitPct, 100) / 100.0));
        if (Has(Control.CoreOffset)) _api.SetGpuFrequencyOffset(_gpu, Clamp(Control.CoreOffset, s.CoreOffsetMhz, 0));
        if (Has(Control.VoltageOffset)) _api.SetGpuVoltageOffset(_gpu, Clamp(Control.VoltageOffset, s.VoltageOffsetMv, 0));
        if (Has(Control.MemoryOffset)) _api.SetVramFrequencyOffset(_gpu, Clamp(Control.MemoryOffset, s.MemoryOffsetMhz, 0));
        var requested = new TuneSettings(
            Has(Control.PowerLimit) && s.PowerLimitPct is { } pl && pl != 100 ? pl : null,
            Has(Control.CoreOffset) && s.CoreOffsetMhz is { } co && co != 0 ? co : null,
            Has(Control.MemoryOffset) && s.MemoryOffsetMhz is { } mo && mo != 0 ? mo : null,
            Has(Control.VoltageOffset) && s.VoltageOffsetMv is { } vo && vo != 0 ? vo : null,
            null);
        return new ApplyResult(requested, await ReadAsync(ct));
    }

    /// <summary>Without the waiver Redline never changed anything, so there's nothing to reset.</summary>
    public Task ResetAsync(CancellationToken ct)
    {
        if (WaiverAccepted) _api.ResetToDefault(_gpu);
        return Task.CompletedTask;
    }

    public Telemetry? ReadTelemetry()
    {
        IgclTelemetry? t;
        try { t = _api.Telemetry(_gpu); } catch (IgclException) { return null; }
        if (t is null) return null;
        var limits = (t.PowerLimited ? LimitReason.Power : 0) | (t.TempLimited ? LimitReason.Temperature : 0)
            | (t.VoltageLimited ? LimitReason.Voltage : 0) | (t.UtilLimited ? LimitReason.Idle : 0);
        return new Telemetry(DateTimeOffset.UtcNow, t.CoreClockMhz ?? 0, t.MemClockMhz ?? 0, t.TempC ?? 0, null, t.PowerW ?? 0,
            t.UsagePct ?? 0, null, t.FanRpm, 0)
        {
            MemTempC = t.MemTempC,
            CoreVoltageMv = t.VoltageMv,
            Limits = limits,
            PowerLimitW = _defaultPower > 0 && Has(Control.PowerLimit) ? Watts(_api.GetPowerLimit(_gpu), _powerUnits) : null,
        };
    }

}
