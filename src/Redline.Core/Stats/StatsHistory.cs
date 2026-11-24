namespace Redline.Core.Stats;

public enum StatsRange { FiveMinutes, Hour, Day }

public sealed record StatPoint(DateTimeOffset At, double Value);

public sealed record StatSeries(string Metric, string Unit, IReadOnlyList<StatPoint> Points, double Min, double Max, double Avg);

/// <summary>
/// Sensor history for the Stats page: every sample for the last hour, plus one-minute averages for 24 hours.
/// </summary>
public sealed class StatsHistory(TimeProvider time)
{
    static readonly TimeSpan Fine = TimeSpan.FromHours(1), Coarse = TimeSpan.FromHours(24);
    readonly object _gate = new();
    readonly Dictionary<string, Queue<StatPoint>> _fine = new();
    readonly Dictionary<string, Queue<StatPoint>> _coarse = new();
    readonly Dictionary<string, (double Sum, int N)> _bucket = new();
    DateTimeOffset _bucketStart = DateTimeOffset.MinValue;

    static readonly Dictionary<string, string> Units = new()
    {
        ["GPU clock"] = "MHz", ["Memory clock"] = "MHz", ["GPU temp"] = "°C", ["Hotspot"] = "°C", ["Memory temp"] = "°C",
        ["Power"] = "W", ["Power limit use"] = "%", ["GPU load"] = "%", ["Fan"] = "%", ["Fan RPM"] = "RPM", ["VRAM"] = "GB",
        ["CPU temp"] = "°C", ["CPU load"] = "%", ["CPU clock"] = "MHz", ["CPU power"] = "W", ["RAM"] = "GB", ["FPS"] = "fps",
    };

    public static string UnitOf(string metric) => Units.GetValueOrDefault(metric, "");

    public void Add(IReadOnlyDictionary<string, double> values)
    {
        var now = time.GetUtcNow();
        lock (_gate)
        {
            if (now - _bucketStart >= TimeSpan.FromMinutes(1)) FlushBucket(now);
            foreach (var (k, v) in values)
            {
                if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                Q(_fine, k).Enqueue(new StatPoint(now, v));
                var b = _bucket.GetValueOrDefault(k);
                _bucket[k] = (b.Sum + v, b.N + 1);
            }
            Trim(_fine, now - Fine);
            Trim(_coarse, now - Coarse);
        }
    }

    void FlushBucket(DateTimeOffset now)
    {
        if (_bucketStart != DateTimeOffset.MinValue)
            foreach (var (k, (sum, n)) in _bucket) if (n > 0) Q(_coarse, k).Enqueue(new StatPoint(_bucketStart, sum / n));
        _bucket.Clear();
        _bucketStart = now;
    }

    static Queue<StatPoint> Q(Dictionary<string, Queue<StatPoint>> d, string k) => d.TryGetValue(k, out var q) ? q : d[k] = new Queue<StatPoint>();

    static void Trim(Dictionary<string, Queue<StatPoint>> d, DateTimeOffset cutoff)
    {
        foreach (var q in d.Values) while (q.Count > 0 && q.Peek().At < cutoff) q.Dequeue();
    }

    public IReadOnlyList<StatSeries> Query(StatsRange range)
    {
        var now = time.GetUtcNow();
        lock (_gate)
        {
            var (source, window) = range switch
            {
                StatsRange.FiveMinutes => (_fine, TimeSpan.FromMinutes(5)),
                StatsRange.Hour => (_fine, TimeSpan.FromHours(1)),
                _ => (_coarse, Coarse),
            };
            var result = new List<StatSeries>();
            foreach (var (k, q) in source)
            {
                var pts = q.Where(p => p.At >= now - window).ToList();
                if (range == StatsRange.Day && _bucket.TryGetValue(k, out var b) && b.N > 0) pts.Add(new StatPoint(_bucketStart, b.Sum / b.N));
                if (pts.Count == 0) continue;
                if (range == StatsRange.Hour && pts.Count > 720) pts = Downsample(pts, 720);
                result.Add(new StatSeries(k, UnitOf(k), pts, pts.Min(p => p.Value), pts.Max(p => p.Value), pts.Average(p => p.Value)));
            }
            return result.OrderBy(s => Array.IndexOf(Units.Keys.ToArray(), s.Metric) is var i && i < 0 ? 99 : i).ToList();
        }
    }

    static List<StatPoint> Downsample(List<StatPoint> pts, int target)
    {
        var size = (int)Math.Ceiling(pts.Count / (double)target);
        return pts.Chunk(size).Select(c => new StatPoint(c[0].At, c.Average(p => p.Value))).ToList();
    }
}
