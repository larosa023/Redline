using Redline.Core.AutoTune;
using Redline.Core.Gpu;
using Redline.Core.Sim;

namespace Redline.Core.Tests.Fakes;

/// <summary>Instant stress test against a SimGpuDevice: unstable above the spec's stable limits.</summary>
public sealed class SimStressRunner : IStressRunner
{
    public int Runs { get; private set; }
    public List<TuneSettings> Tested { get; } = new();
    public Func<int, TimeSpan, bool>? ForceFail { get; set; }   // (run index, duration) → fail?
    public Action<int>? OnRun { get; set; }

    public Task<StressResult> RunAsync(IGpuDevice d, TimeSpan duration, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var idx = Runs++;
        OnRun?.Invoke(idx);
        ct.ThrowIfCancellationRequested();
        var sim = d as SimGpuDevice ?? ((IWrapsSim)d).Inner;
        var s = sim.Current;
        Tested.Add(s);
        var spec = sim.Spec;
        var core = s.CoreOffsetMhz ?? 0;
        var mem = s.MemoryOffsetMhz ?? 0;
        var volt = s.VoltageOffsetMv ?? 0;
        var stable = core <= spec.StableCoreMax && mem <= spec.StableMemMax && volt >= spec.MinStableVoltageMv
                     && !(ForceFail?.Invoke(idx, duration) ?? false);
        var pl = s.PowerLimitPct ?? 100;
        var plFactor = pl >= 80 ? 1 - (100 - pl) * 0.0005 : 0.99 - (80 - pl) * 0.01;
        var score = spec.StockScore * (1 + core / 10000.0 + mem / 50000.0) * Math.Min(plFactor, 1.0);
        var power = spec.StockPowerW * pl / 100.0 * (1 + volt / 1000.0);
        // Memory bandwidth rises with the memory clock; past the peak (GDDR6X error replay) it falls 1.5% per 50 MHz.
        var bw = spec.MemBandwidthPeakMhz is { } peak && mem > peak
            ? 1000 * (1 + peak / 10500.0) * (1 - 0.015 * (mem - peak) / 50.0)
            : 1000 * (1 + mem / 10500.0);
        return Task.FromResult(new StressResult(true, stable ? 0 : 3, score, false, power, 70) { MemBandwidthGBs = bw });
    }
}

public interface IWrapsSim { SimGpuDevice Inner { get; } }
