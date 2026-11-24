using Redline.Core.AutoTune;
using Redline.Core.Gpu;
using Redline.Core.Safety;
using Redline.Core.Stats;

namespace Redline.Engine;

/// <summary>About a minute: 30 s at factory, 30 s with the tune, then back to what was running before.</summary>
public sealed class BeforeAfterRunner(IGpuDevice device, IStressRunner stress, SafeStartJournal journal)
{
    public static TimeSpan Each { get; set; } = TimeSpan.FromSeconds(30);

    public async Task<BeforeAfter> RunAsync(TuneSettings tuned, TuneSettings restoreTo, CancellationToken ct)
    {
        try
        {
            journal.MarkPending(device.Id, TuneSettings.Factory, "before/after: stock");
            await device.ResetAsync(ct);
            var (stock, stockFan) = await Measure(ct);
            journal.MarkPending(device.Id, tuned, "before/after: tuned");
            await device.ApplyAsync(tuned, ct);
            var (tunedR, tunedFan) = await Measure(ct);
            return new BeforeAfter(stock.Score, tunedR.Score, stock.MaxTempC, tunedR.MaxTempC, stock.AvgPowerW, tunedR.AvgPowerW, stockFan, tunedFan);
        }
        finally
        {
            journal.MarkPending(device.Id, restoreTo, "before/after: restore");
            if (restoreTo.IsFactory) await device.ResetAsync(CancellationToken.None);
            else await device.ApplyAsync(restoreTo, CancellationToken.None);
            journal.MarkConfirmed(device.Id, restoreTo);
        }
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
