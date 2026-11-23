using Redline.Core.Stats;
using Redline.Core.Tests.Fakes;
using Xunit;

namespace Redline.Core.Tests.Stats;

public class StatsTests
{
    static StatSample S(FakeTime t, double w, bool game = true, string profile = "send-it", bool crash = false) =>
        new(t.GetUtcNow(), w, game, profile, crash);

    [Fact]
    public void GameTime_SumsGameSamples()
    {
        var t = new FakeTime(); var ws = new WeeklyStats(t);
        for (var i = 0; i < 60; i++) { ws.Add(S(t, 300, game: i < 30), null); t.Advance(TimeSpan.FromMinutes(1)); }
        Assert.Equal(TimeSpan.FromMinutes(30), ws.Summary(0.17m).GameTime);
    }

    [Fact]
    public void EnergySaved_UsesMeasuredStockPower()
    {
        var t = new FakeTime(); var ws = new WeeklyStats(t);
        for (var i = 0; i < 60; i++) { ws.Add(S(t, 200, profile: "whisper"), 300); t.Advance(TimeSpan.FromMinutes(1)); }
        var sum = ws.Summary(0.17m);
        Assert.Equal(0.1, sum.EnergySavedKwh, 3);
        Assert.Equal(0.02m, sum.MoneySaved);
    }

    [Fact]
    public void NoMeasurement_NoSavingsCounted()
    {
        var t = new FakeTime(); var ws = new WeeklyStats(t);
        for (var i = 0; i < 60; i++) { ws.Add(S(t, 200), null); t.Advance(TimeSpan.FromMinutes(1)); }
        Assert.Equal(0, ws.Summary(0.17m).EnergySavedKwh);
    }

    [Fact]
    public void OlderThan7Days_Excluded()
    {
        var t = new FakeTime(); var ws = new WeeklyStats(t);
        ws.Add(S(t, 300, crash: true), null); t.Advance(TimeSpan.FromMinutes(1)); ws.Add(S(t, 300), null);
        t.Advance(TimeSpan.FromDays(8));
        var sum = ws.Summary(0.17m);
        Assert.Equal(TimeSpan.Zero, sum.GameTime);
        Assert.Equal(0, sum.Crashes);
    }

    [Fact]
    public void Crashes_Counted()
    {
        var t = new FakeTime(); var ws = new WeeklyStats(t);
        ws.Add(S(t, 300, crash: true), null); t.Advance(TimeSpan.FromMinutes(1)); ws.Add(S(t, 300), null);
        Assert.Equal(1, ws.Summary(0.17m).Crashes);
    }

    [Fact]
    public void PersonalBest_Uses10sAverage_IgnoresSpikes()
    {
        var pb = new PersonalBest(); var t0 = DateTimeOffset.UnixEpoch;
        for (var i = 0; i < 20; i++) pb.Observe(t0.AddSeconds(i), i == 5 ? 3100 : 2800, stableProfileActive: true);
        Assert.NotNull(pb.Best);
        Assert.InRange(pb.Best!.Value, 2800, 2831);
    }

    [Fact]
    public void PersonalBest_IgnoredWhenUntestedProfile()
    {
        var pb = new PersonalBest(); var t0 = DateTimeOffset.UnixEpoch;
        for (var i = 0; i < 20; i++) pb.Observe(t0.AddSeconds(i), 2900, stableProfileActive: false);
        Assert.Null(pb.Best);
    }

    [Fact]
    public void PersonalBest_FlagsNewBest()
    {
        var pb = new PersonalBest(); var t0 = DateTimeOffset.UnixEpoch;
        for (var i = 0; i < 12; i++) pb.Observe(t0.AddSeconds(i), 2800, true);
        Assert.True(pb.IsNewBest);
        pb.ClearNewBest();
        for (var i = 12; i < 30; i++) pb.Observe(t0.AddSeconds(i), 2790, true);
        Assert.False(pb.IsNewBest);
        for (var i = 30; i < 45; i++) pb.Observe(t0.AddSeconds(i), 2850, true);
        Assert.True(pb.IsNewBest);
    }

    [Fact]
    public void FrameStats_AvgAndOnePercentLow()
    {
        var ft = Enumerable.Repeat(10.0, 99).Append(50.0).ToList();
        var (avg, low) = FrameStats.From(ft);
        Assert.Equal(96.15, avg, 2);
        Assert.Equal(20, low, 2);
    }

    [Fact]
    public void FrameStats_Empty_Zero() => Assert.Equal((0.0, 0.0), FrameStats.From([]));

    [Fact]
    public void BeforeAfter_Changes()
    {
        var ba = new BeforeAfter(1000, 1070, 70, 64, 400, 390, 60, 55);
        Assert.Equal(7, ba.PerfChangePct, 3);
        Assert.Equal(-6, ba.TempChangeC, 3);
        Assert.Equal(-10, ba.PowerChangeW, 3);
    }
}
