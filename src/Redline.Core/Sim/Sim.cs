using Redline.Core.Gpu;

namespace Redline.Core.Sim;

/// <summary>Behaviour of a simulated GPU: what it allows and where it becomes unstable.</summary>
public sealed record SimGpuSpec(string Name, Capabilities Caps, int StableCoreMax, int StableMemMax, int MinStableVoltageMv,
    double StockScore, double StockPowerW, IReadOnlyDictionary<Control, int>? DriverClamp = null, int? MemBandwidthPeakMhz = null);

/// <summary>In-memory GPU used for development, tests and demo mode.</summary>
public sealed class SimGpuDevice(SimGpuSpec spec, string id = "sim-0") : IGpuDevice
{
    readonly object _gate = new();
    TuneSettings _current = TuneSettings.Factory;
    bool _lost;

    public SimGpuSpec Spec => spec;
    public string Id => id;
    public string Name => spec.Name;
    public Vendor Vendor => Vendor.Simulated;
    public string DriverVersion => "1.0";
    public Capabilities Capabilities => spec.Caps;
    public TuneSettings Current { get { lock (_gate) return _current; } }

    public void SimulateLoss(bool lost) { lock (_gate) _lost = lost; }

    public Task<TuneSettings> ReadAsync(CancellationToken ct) => Task.FromResult(Current);

    public Task<ApplyResult> ApplyAsync(TuneSettings s, CancellationToken ct)
    {
        var actual = TuneSettings.Factory;
        foreach (var c in TuneSettings.ScalarControls)
        {
            var v = s.Get(c);
            var range = spec.Caps.Range(c);
            if (v is null || range is null) continue;
            var x = range.Clamp(v.Value);
            if (spec.DriverClamp is { } dc && dc.TryGetValue(c, out var cap)) x = Math.Min(x, cap);
            actual = actual.With(c, x);
        }
        if (s.Fan is not null && spec.Caps.HasFanCurve) actual = actual with { Fan = s.Fan };
        lock (_gate) _current = actual;
        return Task.FromResult(new ApplyResult(s, actual));
    }

    public Task ResetAsync(CancellationToken ct) { lock (_gate) _current = TuneSettings.Factory; return Task.CompletedTask; }

    public Telemetry? ReadTelemetry()
    {
        TuneSettings s;
        lock (_gate) { if (_lost) return null; s = _current; }
        var power = spec.StockPowerW * (s.PowerLimitPct ?? 100) / 100.0;
        var temp = 30 + power / 10.0;
        return new Telemetry(DateTimeOffset.UtcNow, 2685 + (s.CoreOffsetMhz ?? 0), 10501 + (s.MemoryOffsetMhz ?? 0),
            temp, temp + 12, power, 98, 45, 1400, 6144);
    }
}

public sealed class SimGpuBackend(params SimGpuDevice[] devices) : IGpuBackend
{
    public Vendor Vendor => Vendor.Simulated;
    public bool IsAvailable() => true;
    public IReadOnlyList<IGpuDevice> Discover() => devices;
}
