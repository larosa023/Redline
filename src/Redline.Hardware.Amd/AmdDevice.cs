using Redline.Core.Gpu;

namespace Redline.Hardware.Amd;

public sealed class AmdBackend(IAdlxApi api) : IGpuBackend
{
    public Vendor Vendor => Vendor.Amd;

    public bool IsAvailable()
    {
        try { return api.TryLoad() && api.Gpus().Count > 0; }
        catch (Exception e) when (e is AdlxException or DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    public IReadOnlyList<IGpuDevice> Discover() => api.Gpus().Select(g => (IGpuDevice)new AmdDevice(api, g)).ToList();
}

/// <summary>
/// Maps ADLX's absolute values (max clock MHz, max voltage mV, VRAM MHz, power % offset) to Redline's offsets.
/// Factory values are learned while the card is at factory; if another tool already tuned it, the first apply
/// resets to factory once (the user asked for a change) so offsets are measured from the true factory values.
/// Any range that looks wrong is dropped, so a misread can never become a write.
/// </summary>
public sealed class AmdDevice : IGpuDevice
{
    const int CoreFloor = -500;
    readonly IAdlxApi _api;
    readonly int _gpu;
    readonly AdlxRanges _ranges;
    AdlxValues? _factory;

    public AmdDevice(IAdlxApi api, AdlxGpuInfo info)
    {
        _api = api; _gpu = info.Index;
        Name = info.Name; DriverVersion = info.DriverVersion; VramTotalMb = info.TotalVramMb;
        Id = "amd-" + (string.IsNullOrEmpty(info.UniqueId) ? info.Index.ToString() : info.UniqueId);

        var support = Safe(() => api.Support(_gpu)) ?? new AdlxSupport(false, false, false, false);
        _ranges = Safe(() => api.Ranges(_gpu)) ?? new AdlxRanges(null, null, null, null);
        if (Safe(() => api.IsAtFactory(_gpu)) == true) _factory = Safe(() => api.Read(_gpu));
        var basis = _factory ?? Safe(() => api.Read(_gpu));

        var ranges = new Dictionary<Control, ControlRange>();
        if (support.Gfx && support.Gfx2 && basis is not null)
        {
            if (Offset(_ranges.GpuMaxFreq, basis.GpuMaxFreqMhz, 100, 5000, CoreFloor) is { } core) ranges[Control.CoreOffset] = core;
            if (Offset(_ranges.GpuVoltage, basis.GpuVoltageMv, 400, 1500, int.MinValue) is { } volt) ranges[Control.VoltageOffset] = volt;
        }
        if (support.Vram && basis is not null && Offset(_ranges.VramMaxFreq, basis.VramMaxFreqMhz, 100, 5000, int.MinValue) is { } mem)
            ranges[Control.MemoryOffset] = mem;
        if (support.Power && _ranges.PowerPct is { } p && p.Min <= 0 && p.Max >= 0 && p.Min >= -50 && p.Max <= 50 && p.Max > p.Min)
            ranges[Control.PowerLimit] = new(100 + p.Min, 100 + p.Max, Math.Max(1, p.Step), 100);
        Capabilities = new Capabilities(ranges, HasFanCurve: false, Has16PinConnector: false);
    }

    /// <summary>Offset range around the current value, or null when the driver's numbers don't make sense.</summary>
    static ControlRange? Offset(AdlxRange? r, int? basis, int lo, int hi, int floor)
    {
        if (r is null || basis is not { } b) return null;
        if (r.Max <= r.Min || r.Min < lo || r.Max > hi || b < r.Min || b > r.Max) return null;
        return new(Math.Max(r.Min - b, floor), r.Max - b, Math.Max(1, r.Step), 0);
    }

    static T? Safe<T>(Func<T> f) where T : class { try { return f(); } catch (AdlxException) { return null; } }
    static bool? Safe(Func<bool> f) { try { return f(); } catch (AdlxException) { return null; } }

    public string Id { get; }
    public string Name { get; }
    public Vendor Vendor => Vendor.Amd;
    public string DriverVersion { get; }
    public int VramTotalMb { get; }
    public Capabilities Capabilities { get; }

    public Task<TuneSettings> ReadAsync(CancellationToken ct)
    {
        if (_factory is null)
        {
            if (!_api.IsAtFactory(_gpu)) return Task.FromResult(TuneSettings.Factory);   // tuned by another tool: unknown baseline
            _factory = _api.Read(_gpu);
        }
        var v = _api.Read(_gpu);
        var f = _factory;
        return Task.FromResult(new TuneSettings(
            Supported(Control.PowerLimit) && v.PowerPct is { } p && p != 0 ? 100 + p : null,
            Diff(Control.CoreOffset, v.GpuMaxFreqMhz, f.GpuMaxFreqMhz),
            Diff(Control.MemoryOffset, v.VramMaxFreqMhz, f.VramMaxFreqMhz),
            Diff(Control.VoltageOffset, v.GpuVoltageMv, f.GpuVoltageMv),
            null));
    }

    bool Supported(Control c) => Capabilities.Supports(c);
    int? Diff(Control c, int? now, int? factory) => Supported(c) && now is { } n && factory is { } f && n != f ? n - f : null;

    public async Task<ApplyResult> ApplyAsync(TuneSettings s, CancellationToken ct)
    {
        if (_factory is null)
        {
            _api.ResetToFactory(_gpu);
            _factory = _api.Read(_gpu);
        }
        var f = _factory;
        int? Abs(Control c, int? offset, int? factory) => Supported(c) && factory is { } b ? b + Capabilities.Range(c)!.Clamp(offset ?? 0) : null;
        _api.Write(_gpu, new AdlxValues(
            Abs(Control.CoreOffset, s.CoreOffsetMhz, f.GpuMaxFreqMhz),
            Abs(Control.VoltageOffset, s.VoltageOffsetMv, f.GpuVoltageMv),
            Abs(Control.MemoryOffset, s.MemoryOffsetMhz, f.VramMaxFreqMhz),
            Supported(Control.PowerLimit) ? Capabilities.Range(Control.PowerLimit)!.Clamp(s.PowerLimitPct ?? 100) - 100 : null));
        var requested = new TuneSettings(
            Supported(Control.PowerLimit) && s.PowerLimitPct is { } pl && pl != 100 ? pl : null,
            Supported(Control.CoreOffset) && s.CoreOffsetMhz is { } co && co != 0 ? co : null,
            Supported(Control.MemoryOffset) && s.MemoryOffsetMhz is { } mo && mo != 0 ? mo : null,
            Supported(Control.VoltageOffset) && s.VoltageOffsetMv is { } vo && vo != 0 ? vo : null,
            null);
        return new ApplyResult(requested, await ReadAsync(ct));
    }

    public Task ResetAsync(CancellationToken ct)
    {
        _api.ResetToFactory(_gpu);
        _factory ??= _api.Read(_gpu);
        return Task.CompletedTask;
    }

    public Telemetry? ReadTelemetry()
    {
        AdlxMetrics? m;
        try { m = _api.Metrics(_gpu); } catch (AdlxException) { return null; }
        if (m is null) return null;
        return new Telemetry(DateTimeOffset.UtcNow, m.ClockMhz, m.VramClockMhz, m.TempC, m.HotspotC, m.BoardPowerW ?? m.PowerW,
            m.UsagePct, null, m.FanRpm, m.VramUsedMb)
        {
            VramTotalMb = VramTotalMb,
            CoreVoltageMv = m.VoltageMv,
        };
    }
}
