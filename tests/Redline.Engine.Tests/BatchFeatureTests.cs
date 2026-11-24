using Redline.Core.Gpu;
using Redline.Core.Stats;
using Redline.Core.Storage;
using Redline.Ipc;
using Xunit;

namespace Redline.Engine.Tests;

public sealed class FakeExtras : IGpuExtraSensors
{
    public GpuExtras? Read(string gpuName) => new(HotspotC: 77, MemTempC: 68, FanRpm: 1450, CoreVoltageMv: 1050);
}

public sealed class FakeFrames : IFrameSource
{
    public event Action<string, IReadOnlyList<double>>? Frames;
    public void Push(string proc, IReadOnlyList<double> ft) => Frames?.Invoke(proc, ft);
}

public class BatchFeatureTests : IAsyncLifetime
{
    readonly EngineFixture F = new();
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await F.DisposeAsync();

    [Fact]
    public async Task GetSeries_AfterTicks_HasGpuClockAndTemp()
    {
        await F.StartAsync();
        for (var i = 0; i < 5; i++) { F.Time.Advance(TimeSpan.FromSeconds(5)); await F.Engine.IdleAsync(); }
        var r = (await F.Send(MessageTypes.GetSeries, new SeriesRequest(StatsRange.FiveMinutes))).PayloadAs<SeriesResponse>()!;
        var clock = r.Series.Single(s => s.Metric == "GPU clock");
        Assert.Equal(5, clock.Points.Count);
        Assert.Contains(r.Series, s => s.Metric == "CPU temp");
    }

    [Fact]
    public async Task Extras_MergedIntoTelemetry()
    {
        await F.StartAsync(extras: new FakeExtras());
        F.Time.Advance(TimeSpan.FromSeconds(5)); await F.Engine.IdleAsync();
        var t = F.Events.Last(e => e.Type == MessageTypes.Telemetry).PayloadAs<TelemetryEvent>()!.Gpu!;
        Assert.Equal(77, t.HotspotC);
        Assert.Equal(68, t.MemTempC);
        Assert.Equal(1450, t.FanRpm);
    }

    [Fact]
    public async Task Frames_EmitFpsSample_AndHistory()
    {
        var frames = new FakeFrames();
        await F.StartAsync(frames: frames);
        frames.Push("Cyberpunk2077.exe", Enumerable.Repeat(10.0, 99).Append(50.0).ToList());
        var ev = F.Events.Last(e => e.Type == MessageTypes.FpsSample).PayloadAs<FpsSampleEvent>()!;
        Assert.Equal("Cyberpunk2077.exe", ev.ProcessName);
        Assert.Equal(20, ev.OnePercentLowFps, 1);
        F.Time.Advance(TimeSpan.FromSeconds(5)); await F.Engine.IdleAsync();
        var r = (await F.Send(MessageTypes.GetSeries, new SeriesRequest(StatsRange.FiveMinutes))).PayloadAs<SeriesResponse>()!;
        Assert.Contains(r.Series, s => s.Metric == "FPS");
    }

    [Fact]
    public async Task ImportProfile_AddsUntested_Clamped()
    {
        await F.StartAsync();
        var p = new Profile("x", "Friend's tune", "bolt", TuneSettings.Factory with { CoreOffsetMhz = 480 }, null, true, DateTimeOffset.UnixEpoch, null);
        var s = (await F.Send(MessageTypes.ImportProfile, new ImportProfileRequest(p))).PayloadAs<HelperState>()!;
        var added = s.Profiles.Single(x => x.Name == "Friend's tune");
        Assert.False(added.StabilityTested);
        Assert.Equal(300, added.Gpu.CoreOffsetMhz);   // clamped to the safe range
    }

    [Fact]
    public async Task BeforeAfter_StoresMeasured_AndRestoresPrevious()
    {
        var store = new ProfileStore(F.DataDir);
        store.Update(store.Get("send-it") with { Gpu = TuneSettings.Factory with { CoreOffsetMhz = 150 }, StabilityTested = true });
        await F.StartAsync();
        await F.Send(MessageTypes.ApplyProfile, new ApplyProfileRequest("gpu0", "whisper"));
        var before = F.Gpu.Current;
        await F.Send(MessageTypes.RunBeforeAfter, new BeforeAfterRequest("gpu0", "send-it"));
        await F.Engine.BeforeAfterTask!;
        await F.Engine.IdleAsync();
        Assert.Equal(before, F.Gpu.Current);
        var p = (await F.State()).Profiles.Single(x => x.Id == "send-it");
        Assert.NotNull(p.Measured);
        Assert.True(p.Measured!.PerfChangePct > 0);
        Assert.Contains(F.Events, e => e.Type == MessageTypes.BeforeAfterResult);
    }
}
