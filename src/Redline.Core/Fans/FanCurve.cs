namespace Redline.Core.Fans;

public sealed record FanPoint(int TempC, int Pct);

/// <summary>Fan curve: fan percent by GPU temperature.</summary>
public sealed record FanCurve(IReadOnlyList<FanPoint> Points)
{
    public bool Equals(FanCurve? other) => other is not null && Points.SequenceEqual(other.Points);
    public override int GetHashCode() => Points.Aggregate(17, (h, p) => h * 31 + p.GetHashCode());
}
