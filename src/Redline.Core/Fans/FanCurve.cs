namespace Redline.Core.Fans;

public sealed record FanPoint(int TempC, int Pct);

/// <summary>Fan curve: fan percent by GPU temperature, linear between points, flat beyond the ends.</summary>
public sealed record FanCurve(IReadOnlyList<FanPoint> Points)
{
    public static FanCurve Default { get; } = new([new(30, 30), new(50, 40), new(65, 55), new(75, 75), new(85, 100)]);

    public bool Equals(FanCurve? other) => other is not null && Points.SequenceEqual(other.Points);
    public override int GetHashCode() => Points.Aggregate(17, (h, p) => h * 31 + p.GetHashCode());

    /// <summary>Problems that make this curve unsafe or unusable; empty when valid.</summary>
    public IReadOnlyList<string> Validate(int minPct)
    {
        var e = new List<string>();
        if (Points.Count is < 2 or > 8) e.Add("A fan curve needs between 2 and 8 points.");
        for (var i = 0; i < Points.Count; i++)
        {
            var p = Points[i];
            if (p.TempC is < 20 or > 100) e.Add($"Point {i + 1}: temperature must be between 20 and 100 °C.");
            if (p.Pct < minPct) e.Add($"Point {i + 1}: your card's fans can't go below {minPct}%.");
            if (p.Pct > 100) e.Add($"Point {i + 1}: fan speed can't exceed 100%.");
            if (p.TempC >= 85 && p.Pct < 80) e.Add($"Point {i + 1}: at 85 °C and above, fans must run at least 80%.");
            if (i > 0 && p.TempC <= Points[i - 1].TempC) e.Add($"Point {i + 1}: each point must be hotter than the one before.");
            if (i > 0 && p.Pct < Points[i - 1].Pct) e.Add($"Point {i + 1}: fan speed can't drop as temperature rises.");
        }
        return e;
    }

    public int Evaluate(double tempC)
    {
        if (Points.Count == 0) return 100;
        if (tempC <= Points[0].TempC) return Points[0].Pct;
        for (var i = 1; i < Points.Count; i++)
        {
            var a = Points[i - 1];
            var b = Points[i];
            if (tempC <= b.TempC)
            {
                var f = (tempC - a.TempC) / (b.TempC - a.TempC);
                return (int)Math.Round(a.Pct + f * (b.Pct - a.Pct), MidpointRounding.AwayFromZero);
            }
        }
        return Points[^1].Pct;
    }
}
