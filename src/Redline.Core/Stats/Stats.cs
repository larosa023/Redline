namespace Redline.Core.Stats;

public sealed record StatSample(DateTimeOffset At, double PowerW, bool GameRunning, string ProfileId, bool CrashOrReset);

public sealed record WeeklySummary(TimeSpan GameTime, double EnergySavedKwh, decimal MoneySaved, int Crashes);

/// <summary>Rolling 7-day recap: game time, energy and money saved versus measured stock power, crashes.</summary>
public sealed class WeeklyStats(TimeProvider time)
{
    static readonly TimeSpan Window = TimeSpan.FromDays(7), MaxGap = TimeSpan.FromMinutes(5);
    readonly List<(StatSample S, double? StockW)> _samples = new();
    readonly object _gate = new();

    public void Add(StatSample s, double? stockPowerWForProfile)
    {
        lock (_gate)
        {
            _samples.Add((s, stockPowerWForProfile));
            var cutoff = time.GetUtcNow() - Window;
            _samples.RemoveAll(x => x.S.At < cutoff);
        }
    }

    public WeeklySummary Summary(decimal ratePerKwh)
    {
        var now = time.GetUtcNow();
        var cutoff = now - Window;
        lock (_gate)
        {
            var list = _samples.Where(x => x.S.At >= cutoff).OrderBy(x => x.S.At).ToList();
            var game = TimeSpan.Zero; double wh = 0; var crashes = 0;
            for (var i = 0; i < list.Count; i++)
            {
                var (s, stock) = list[i];
                var end = i + 1 < list.Count ? list[i + 1].S.At : now;
                var dt = end - s.At;
                if (dt > MaxGap) dt = MaxGap;
                if (dt < TimeSpan.Zero) dt = TimeSpan.Zero;
                if (s.GameRunning) game += dt;
                if (stock is { } sw) wh += (sw - s.PowerW) * dt.TotalHours;
                if (s.CrashOrReset) crashes++;
            }
            var kwh = Math.Max(0, wh / 1000);
            return new WeeklySummary(game, kwh, Math.Round((decimal)kwh * ratePerKwh, 2, MidpointRounding.AwayFromZero), crashes);
        }
    }
}

/// <summary>Highest 10-second average core clock seen while a stability-tested profile is active.</summary>
public sealed class PersonalBest
{
    static readonly TimeSpan Span = TimeSpan.FromSeconds(9);
    readonly Queue<(DateTimeOffset At, double Mhz)> _window = new();

    public double? Best { get; private set; }
    public bool IsNewBest { get; private set; }
    public void ClearNewBest() => IsNewBest = false;

    public void Restore(double? best) => Best = best;

    public void Observe(DateTimeOffset at, double coreMhz, bool stableProfileActive)
    {
        if (!stableProfileActive) { _window.Clear(); return; }
        _window.Enqueue((at, coreMhz));
        while (_window.Count > 0 && at - _window.Peek().At > Span) _window.Dequeue();
        if (_window.Count == 0 || at - _window.Peek().At < Span) return;
        var avg = _window.Average(x => x.Mhz);
        if (Best is null || avg > Best.Value) { Best = avg; IsNewBest = true; }
    }
}

public sealed record BeforeAfter(double StockScore, double TunedScore, double StockTempC, double TunedTempC,
    double StockPowerW, double TunedPowerW, double StockFanPct, double TunedFanPct)
{
    public double PerfChangePct => StockScore <= 0 ? 0 : (TunedScore / StockScore - 1) * 100;
    public double TempChangeC => TunedTempC - StockTempC;
    public double PowerChangeW => TunedPowerW - StockPowerW;
}

public static class FrameStats
{
    /// <summary>Average FPS and 1% low FPS (FPS at the 99th-percentile frame time) from frame times in ms.</summary>
    public static (double AvgFps, double OnePercentLowFps) From(IReadOnlyList<double> frameTimesMs)
    {
        if (frameTimesMs.Count == 0) return (0, 0);
        var avg = 1000.0 / frameTimesMs.Average();
        var sorted = frameTimesMs.OrderByDescending(x => x).ToList();
        var n = Math.Max(1, (int)Math.Ceiling(sorted.Count * 0.01));
        var worst = sorted.Take(n).Average();
        return (avg, 1000.0 / worst);
    }
}
