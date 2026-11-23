using System.Text.RegularExpressions;
using Redline.Core.Gpu;

namespace Redline.Hardware.Nvidia;

public static partial class ConnectorTable
{
    /// <summary>Desktop RTX 40/50 series cards use the 12VHPWR / 12V-2x6 16-pin connector.</summary>
    public static bool Has16Pin(string name) =>
        !name.Contains("Laptop", StringComparison.OrdinalIgnoreCase) && Rtx4050().IsMatch(name);

    [GeneratedRegex(@"RTX\s*(40|50)\d\d", RegexOptions.IgnoreCase)]
    private static partial Regex Rtx4050();
}

public sealed class NvidiaBackend(INvmlApi api) : IGpuBackend
{
    bool _initialized;
    public Vendor Vendor => Vendor.Nvidia;

    public bool IsAvailable()
    {
        if (!api.TryLoad()) return false;
        try { Init(); return true; } catch (NvmlException) { return false; }
    }

    void Init()
    {
        if (_initialized) return;
        api.Init();
        _initialized = true;
    }

    public IReadOnlyList<IGpuDevice> Discover()
    {
        Init();
        var driver = api.DriverVersion();
        var list = new List<IGpuDevice>();
        for (var i = 0; i < api.DeviceCount(); i++)
        {
            var h = api.Handle(i);
            list.Add(new NvidiaDevice(api, h, driver));
        }
        return list;
    }
}

/// <summary>One NVIDIA GPU via NVML. Fan curves are run by the engine through <see cref="IFanPercentControl"/>.</summary>
public sealed class NvidiaDevice : IGpuDevice, IFanPercentControl
{
    const int CoreStep = 15, MemStep = 50;
    readonly INvmlApi _api;
    readonly nint _h;
    readonly uint _defaultMw;
    readonly bool _newOffsets;
    TuneSettings _lastFan = TuneSettings.Factory;

    public NvidiaDevice(INvmlApi api, nint handle, string driverVersion)
    {
        _api = api; _h = handle; DriverVersion = driverVersion;
        Name = api.DeviceName(handle);
        Id = "nvidia-" + api.Uuid(handle);
        var ranges = new Dictionary<Control, ControlRange>();

        uint defaultMw = 0;
        try
        {
            defaultMw = api.PowerDefaultLimitMw(handle);
            var (min, max) = api.PowerLimitConstraints(handle);
            if (defaultMw > 0 && max > min)
                ranges[Control.PowerLimit] = new((int)Math.Ceiling(min * 100.0 / defaultMw), (int)Math.Floor(max * 100.0 / defaultMw), 1, 100);
        }
        catch (NvmlException) { }
        _defaultMw = defaultMw;

        try { api.GetClockOffsets(handle, NvmlClockType.Graphics); _newOffsets = true; }
        catch (NvmlException e) when (e.Unsupported) { _newOffsets = false; }
        catch (NvmlException) { }
        AddOffsetRange(ranges, Control.CoreOffset, NvmlClockType.Graphics, CoreStep);
        AddOffsetRange(ranges, Control.MemoryOffset, NvmlClockType.Memory, MemStep);

        var fans = false;
        try
        {
            if (api.FanCount(handle) > 0)
            {
                var (fmin, fmax) = api.FanMinMax(handle);
                ranges[Control.FanSpeed] = new((int)fmin, (int)fmax, 1, 0);
                fans = true;
            }
        }
        catch (NvmlException) { }

        Capabilities = new Capabilities(ranges, HasFanCurve: fans, Has16PinConnector: ConnectorTable.Has16Pin(Name));
    }

    void AddOffsetRange(Dictionary<Control, ControlRange> ranges, Control c, NvmlClockType t, int step)
    {
        try
        {
            var (min, max) = _newOffsets ? (_api.GetClockOffsets(_h, t) is var o ? (o.Min, o.Max) : default) : _api.GetVfOffsetRange(_h, t);
            if (max > 0) ranges[c] = new(min, max, step, 0);
        }
        catch (NvmlException) { }
    }

    public string Id { get; }
    public string Name { get; }
    public Vendor Vendor => Vendor.Nvidia;
    public string DriverVersion { get; }
    public Capabilities Capabilities { get; }

    int GetOffset(NvmlClockType t) => _newOffsets ? _api.GetClockOffsets(_h, t).Current : _api.GetVfOffset(_h, t);

    void SetOffset(NvmlClockType t, int mhz)
    {
        if (_newOffsets) _api.SetClockOffsets(_h, t, mhz);
        else _api.SetVfOffset(_h, t, mhz);
    }

    public Task<TuneSettings> ReadAsync(CancellationToken ct)
    {
        int? power = null, core = null, mem = null;
        if (Capabilities.Supports(Control.PowerLimit) && _defaultMw > 0)
        {
            var p = (int)Math.Round(_api.PowerLimitMw(_h) * 100.0 / _defaultMw);
            power = p == 100 ? null : p;
        }
        if (Capabilities.Supports(Control.CoreOffset)) core = NullIfZero(GetOffset(NvmlClockType.Graphics));
        if (Capabilities.Supports(Control.MemoryOffset)) mem = NullIfZero(GetOffset(NvmlClockType.Memory));
        return Task.FromResult(new TuneSettings(power, core, mem, null, _lastFan.Fan));
    }

    static int? NullIfZero(int v) => v == 0 ? null : v;

    public async Task<ApplyResult> ApplyAsync(TuneSettings s, CancellationToken ct)
    {
        var requested = new TuneSettings(
            s.PowerLimitPct == 100 ? null : s.PowerLimitPct, NullIfZero(s.CoreOffsetMhz ?? 0), NullIfZero(s.MemoryOffsetMhz ?? 0), null,
            Capabilities.HasFanCurve ? s.Fan : null);
        if (Capabilities.Supports(Control.PowerLimit) && _defaultMw > 0)
            _api.SetPowerLimitMw(_h, (uint)Math.Round(_defaultMw * (s.PowerLimitPct ?? 100) / 100.0));
        if (Capabilities.Supports(Control.CoreOffset)) SetOffset(NvmlClockType.Graphics, s.CoreOffsetMhz ?? 0);
        if (Capabilities.Supports(Control.MemoryOffset)) SetOffset(NvmlClockType.Memory, s.MemoryOffsetMhz ?? 0);
        _lastFan = requested;
        if (requested.Fan is null) await SetFanAutoAsync(ct);
        return new ApplyResult(requested, await ReadAsync(ct));
    }

    public async Task ResetAsync(CancellationToken ct) => await ApplyAsync(TuneSettings.Factory, ct);

    public Task SetFanPercentAsync(int pct, CancellationToken ct)
    {
        if (!Capabilities.Supports(Control.FanSpeed)) return Task.CompletedTask;
        var r = Capabilities.Range(Control.FanSpeed)!;
        for (uint i = 0; i < _api.FanCount(_h); i++) _api.SetFanSpeed(_h, i, (uint)r.Clamp(pct));
        return Task.CompletedTask;
    }

    public Task SetFanAutoAsync(CancellationToken ct)
    {
        if (!Capabilities.Supports(Control.FanSpeed)) return Task.CompletedTask;
        for (uint i = 0; i < _api.FanCount(_h); i++) _api.SetDefaultFanSpeed(_h, i);
        return Task.CompletedTask;
    }

    public Telemetry? ReadTelemetry()
    {
        try
        {
            var util = _api.Utilization(_h);
            var (used, _) = _api.Memory(_h);
            double? fan = null;
            try { if (_api.FanCount(_h) > 0) fan = _api.FanSpeed(_h, 0); } catch (NvmlException) { }
            return new Telemetry(DateTimeOffset.UtcNow, _api.ClockMhz(_h, NvmlClockType.Graphics), _api.ClockMhz(_h, NvmlClockType.Memory),
                _api.TemperatureC(_h), null, _api.PowerUsageMw(_h) / 1000.0, util.Gpu, fan, null, used / (1024.0 * 1024.0));
        }
        catch (NvmlException) { return null; }
    }
}
