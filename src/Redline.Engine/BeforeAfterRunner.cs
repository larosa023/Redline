using Redline.Core.AutoTune;
using Redline.Core.Gpu;
using Redline.Core.Safety;
using Redline.Core.Stats;

namespace Redline.Engine;

/// <summary>
/// About two minutes: a short warm-up at factory (not counted), then factory and the tune twice each, alternating,
/// so both see the same temperatures and background load. Then back to what was running before.
/// </summary>
public sealed class BeforeAfterRunner(IGpuDevice device, IStressRunner stress, SafeStartJournal journal)
{
    public static TimeSpan Each { get; set; } = TimeSpan.FromSeconds(20);
    public static TimeSpan WarmUp { get; set; } = TimeSpan.FromSeconds(20);
    const int Rounds = 2;

    public async Task<BeforeAfter> RunAsync(TuneSettings tuned, TuneSettings restoreTo, CancellationToken ct)
    {
        try
        {
            await SetAsync(TuneSettings.Factory, "before/after: warm-up", ct);
            await stress.RunAsync(device, WarmUp, ct);
            var stock = new List<(StressResult R, double Fan)>();
            var tune = new List<(StressResult R, double Fan)>();
            for (var i = 0; i < Rounds; i++)
            {
                await SetAsync(TuneSettings.Factory, "before/after: stock", ct);
                stock.Add(await Measure(ct));
                await SetAsync(tuned, "before/after: tuned", ct);
                tune.Add(await Measure(ct));
            }
            return new BeforeAfter(stock.Average(x => x.R.Score), tune.Average(x => x.R.Score),
                stock.Max(x => x.R.MaxTempC), tune.Max(x => x.R.MaxTempC),
                stock.Average(x => x.R.AvgPowerW), tune.Average(x => x.R.AvgPowerW),
                stock.Average(x => x.Fan), tune.Average(x => x.Fan));
        }
        finally
        {
            journal.MarkPending(device.Id, restoreTo, "before/after: restore");
            if (restoreTo.IsFactory) await device.ResetAsync(CancellationToken.None);
            else await device.ApplyAsync(restoreTo, CancellationToken.None);
            journal.MarkConfirmed(device.Id, restoreTo);
        }
    }

    async Task SetAsync(TuneSettings s, string why, CancellationToken ct)
    {
        journal.MarkPending(device.Id, s, why);
        if (s.IsFactory) await device.ResetAsync(ct);
        else await device.ApplyAsync(s, ct);
    }
    async Task<(StressResult, double Fan)> Measure(CancellationToken ct)
    {
        var r = await stress.RunAsync(device, Each, ct);
        if (r.DriverReset || r.Errors > 0) throw new InvalidOperationException("The tune wasn't stable during the check. Try running auto-tune again.");
        var t = device.ReadTelemetry();
        var temp = r.MaxTempC > 0 ? r.MaxTempC : t?.TempC ?? 0;
        var power = r.AvgPowerW > 0 ? r.AvgPowerW : t?.PowerW ?? 0;
        return (r with { MaxTempC = temp, AvgPowerW = power }, t?.FanPct ?? 0);
    }
}
