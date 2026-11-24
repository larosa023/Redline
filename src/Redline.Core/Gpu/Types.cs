using Redline.Core.Fans;

namespace Redline.Core.Gpu;

public enum Vendor { Nvidia, Amd, Intel, Simulated }

/// <summary>Tunable controls. Units: PowerLimit = percent of default; offsets = MHz; VoltageOffset = mV; FanSpeed = percent.</summary>
public enum Control { PowerLimit, CoreOffset, MemoryOffset, VoltageOffset, FanSpeed }

public sealed record ControlRange(int Min, int Max, int Step, int Default)
{
    public int Clamp(int v) => Math.Clamp(v, Min, Max);
}

public sealed record Capabilities(IReadOnlyDictionary<Control, ControlRange> Ranges, bool HasFanCurve, bool Has16PinConnector)
{
    public static Capabilities MonitorOnly { get; } = new(new Dictionary<Control, ControlRange>(), false, false);
    public bool Supports(Control c) => Ranges.ContainsKey(c);
    public ControlRange? Range(Control c) => Ranges.TryGetValue(c, out var r) ? r : null;
}

/// <summary>A set of requested or applied values. Null means "factory / not changed".</summary>
public sealed record TuneSettings(int? PowerLimitPct, int? CoreOffsetMhz, int? MemoryOffsetMhz, int? VoltageOffsetMv, FanCurve? Fan)
{
    public static TuneSettings Factory { get; } = new(null, null, null, null, null);
    public bool IsFactory => PowerLimitPct is null && CoreOffsetMhz is null && MemoryOffsetMhz is null && VoltageOffsetMv is null && Fan is null;

    public int? Get(Control c) => c switch
    {
        Control.PowerLimit => PowerLimitPct,
        Control.CoreOffset => CoreOffsetMhz,
        Control.MemoryOffset => MemoryOffsetMhz,
        Control.VoltageOffset => VoltageOffsetMv,
        _ => null,
    };

    public TuneSettings With(Control c, int? v) => c switch
    {
        Control.PowerLimit => this with { PowerLimitPct = v },
        Control.CoreOffset => this with { CoreOffsetMhz = v },
        Control.MemoryOffset => this with { MemoryOffsetMhz = v },
        Control.VoltageOffset => this with { VoltageOffsetMv = v },
        _ => this,
    };

    public static IReadOnlyList<Control> ScalarControls { get; } =
        [Control.PowerLimit, Control.CoreOffset, Control.MemoryOffset, Control.VoltageOffset];
}

public sealed record Telemetry(DateTimeOffset At, double CoreClockMhz, double MemClockMhz, double TempC, double? HotspotC,
    double PowerW, double UsagePct, double? FanPct, double? FanRpm, double VramUsedMb)
{
    public double? MemTempC { get; init; }
    public double? VramTotalMb { get; init; }
    public double? PowerLimitW { get; init; }
    public double? CoreVoltageMv { get; init; }
    public LimitReason Limits { get; init; }
    public double? PowerLimitUsePct => PowerLimitW is > 0 ? PowerW / PowerLimitW * 100 : null;
}

/// <summary>What is currently holding the GPU's clocks back.</summary>
[Flags]
public enum LimitReason { None = 0, Idle = 1, Power = 2, Temperature = 4, Voltage = 8, Slowdown = 16 }

public sealed record GpuExtras(double? HotspotC, double? MemTempC, double? FanRpm, double? CoreVoltageMv);

/// <summary>Extra GPU sensors the vendor library doesn't expose (hotspot, memory junction, fan RPM), matched by GPU name.</summary>
public interface IGpuExtraSensors
{
    GpuExtras? Read(string gpuName);
}

public sealed record ApplyResult(TuneSettings Requested, TuneSettings Actual)
{
    public bool Matches => Requested == Actual;
}

public interface IGpuDevice
{
    string Id { get; }
    string Name { get; }
    Vendor Vendor { get; }
    string DriverVersion { get; }
    Capabilities Capabilities { get; }
    Task<TuneSettings> ReadAsync(CancellationToken ct);
    Task<ApplyResult> ApplyAsync(TuneSettings s, CancellationToken ct);
    Task ResetAsync(CancellationToken ct);
    /// <summary>Latest readings, or null when the device is currently unavailable.</summary>
    Telemetry? ReadTelemetry();
}

public interface IGpuBackend
{
    Vendor Vendor { get; }
    bool IsAvailable();
    IReadOnlyList<IGpuDevice> Discover();
}

public sealed record DiscoveryResult(IReadOnlyList<IGpuDevice> Devices, IReadOnlyList<(Vendor Vendor, string Error)> Errors)
{
    /// <summary>Vendors whose driver didn't answer in time and were skipped.</summary>
    public IReadOnlyList<Vendor> TimedOut { get; init; } = [];
}

public static class GpuDiscovery
{
    public static readonly TimeSpan DefaultPerVendorTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Asks each vendor's backend for its GPUs. Each runs on its own thread with a time limit: a vendor driver call
    /// that hangs (seen with AMD's ADLX on a PC with only a CPU's built-in Radeon) is skipped instead of blocking startup.
    /// </summary>
    public static DiscoveryResult DiscoverAll(IEnumerable<IGpuBackend> backends, TimeSpan? perVendorTimeout = null)
    {
        var timeout = perVendorTimeout ?? DefaultPerVendorTimeout;
        var devices = new List<IGpuDevice>();
        var errors = new List<(Vendor, string)>();
        var timedOut = new List<Vendor>();
        foreach (var b in backends)
        {
            IReadOnlyList<IGpuDevice>? found = null;
            Exception? error = null;
            var t = new Thread(() =>
            {
                try
                {
                    using var _ = Diagnostics.HelperLog.Time($"discover {b.Vendor}");
                    found = b.IsAvailable() ? b.Discover() : [];
                }
                catch (Exception e) { error = e; }
            }) { IsBackground = true, Name = $"Redline discover {b.Vendor}" };
            t.Start();
            if (!t.Join(timeout))
            {
                timedOut.Add(b.Vendor);
                Diagnostics.HelperLog.Write($"discover {b.Vendor}: no answer after {timeout.TotalSeconds:0.#} s, skipped");
                continue;
            }
            if (error is not null) errors.Add((b.Vendor, error.Message));
            else if (found is not null) devices.AddRange(found);
        }
        return new DiscoveryResult(devices, errors) { TimedOut = timedOut };
    }
}
