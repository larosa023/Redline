using Redline.Core.Stats;
using Redline.Core.Tests.Fakes;
using Xunit;

namespace Redline.Core.Tests.Stats;

public class StatsHistoryTests
{
    static Dictionary<string, double> M(double clock, double temp) => new() { ["GPU clock"] = clock, ["GPU temp"] = temp };

    [Fact]
    public void Query_5Minutes_ReturnsPointsAndSummary()
    {
        var t = new FakeTime(); var h = new StatsHistory(t);
        for (var i = 0; i < 600; i++) { h.Add(M(2700 + i % 10, 60 + i % 3)); t.Advance(TimeSpan.FromSeconds(1)); }
        var s = h.Query(StatsRange.FiveMinutes).Single(x => x.Metric == "GPU clock");
        Assert.InRange(s.Points.Count, 290, 301);
        Assert.Equal(2700, s.Min);
        Assert.Equal(2709, s.Max);
        Assert.InRange(s.Avg, 2704, 2705);
    }

    [Fact]
    public void Query_24Hours_UsesMinuteAverages()
    {
        var t = new FakeTime(); var h = new StatsHistory(t);
        for (var i = 0; i < 3 * 3600; i += 5) { h.Add(M(2800, 70)); t.Advance(TimeSpan.FromSeconds(5)); }
        var s = h.Query(StatsRange.Day).Single(x => x.Metric == "GPU temp");
        Assert.InRange(s.Points.Count, 170, 181);
        Assert.Equal(70, s.Avg, 3);
    }

    [Fact]
    public void OldData_Dropped()
    {
        var t = new FakeTime(); var h = new StatsHistory(t);
        h.Add(M(2000, 50));
        t.Advance(TimeSpan.FromHours(25));
        h.Add(M(2800, 70));
        var s = h.Query(StatsRange.Day).Single(x => x.Metric == "GPU clock");
        Assert.Equal(2800, s.Min);
    }

    [Fact]
    public void MissingMetric_InSomeSamples_Ok()
    {
        var t = new FakeTime(); var h = new StatsHistory(t);
        h.Add(new Dictionary<string, double> { ["FPS"] = 120 }); t.Advance(TimeSpan.FromSeconds(1));
        h.Add(M(2800, 70)); t.Advance(TimeSpan.FromSeconds(1));
        var all = h.Query(StatsRange.FiveMinutes);
        Assert.Single(all.Single(x => x.Metric == "FPS").Points);
        Assert.Single(all.Single(x => x.Metric == "GPU clock").Points);
    }

    [Fact]
    public void Units_Known() => Assert.Equal("°C", StatsHistory.UnitOf("Hotspot"));
}
