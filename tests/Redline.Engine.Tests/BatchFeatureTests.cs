using Redline.Core.Sim;
using Redline.Core.Tests.Fakes;
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
    public bool Enabled;
    public void SetEnabled(bool enabled) => Enabled = enabled;
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
    public async Task FrameSource_FollowsOverlaySetting()
    {
        var frames = new FakeFrames();
        await F.StartAsync(frames: frames);
        Assert.False(frames.Enabled);
        await F.Send(MessageTypes.UpdateSettings, new SettingsRequest(new AppSettings { OverlayEnabled = true }));
        Assert.True(frames.Enabled);
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

public class ReportExportTests
{
    [Fact]
    public async Task ExportReport_ReturnsHelperFiles_WithSystemSummary()
    {
        await using var f = new EngineFixture();
        await f.StartAsync();
        File.WriteAllText(Path.Combine(f.DataDir, "install.log"), "installed");
        File.WriteAllText(Path.Combine(f.DataDir, "notes.exe"), "not included");

        var r = (await f.Send(MessageTypes.ExportReport)).PayloadAs<ReportResponse>()!;

        Assert.Contains("system.json", r.Files.Keys);
        Assert.Contains("tempCeilingC", r.Files["system.json"]);   // settings travel in the summary even before settings.json exists
        Assert.Equal("installed", r.Files["install.log"]);
        Assert.DoesNotContain("notes.exe", r.Files.Keys);
        Assert.Contains(f.Gpu.Name, r.Files["system.json"]);
        Assert.Contains("helperVersion", r.Files["system.json"]);
    }
}

public class WaiverTests
{
    sealed class WaiverGpu(SimGpuDevice inner) : IGpuDevice, IRequiresWaiver
    {
        public int Accepts;
        public bool WaiverAccepted { get; private set; }
        public void AcceptWaiver() { Accepts++; WaiverAccepted = true; }
        public string Id => inner.Id; public string Name => inner.Name; public Vendor Vendor => Vendor.Intel;
        public string DriverVersion => inner.DriverVersion; public Capabilities Capabilities => inner.Capabilities;
        public Task<TuneSettings> ReadAsync(CancellationToken ct) => inner.ReadAsync(ct);
        public Task<ApplyResult> ApplyAsync(TuneSettings s, CancellationToken ct) => inner.ApplyAsync(s, ct);
        public Task ResetAsync(CancellationToken ct) => inner.ResetAsync(ct);
        public Telemetry? ReadTelemetry() => inner.ReadTelemetry();
    }

    sealed class OneGpu(IGpuDevice d) : IGpuBackend
    {
        public Vendor Vendor => Vendor.Intel;
        public bool IsAvailable() => true;
        public IReadOnlyList<IGpuDevice> Discover() => [d];
    }

    static async Task<Redline.Engine.Engine> Start(EngineFixture f, IGpuDevice d)
    {
        var e = new Redline.Engine.Engine(new EngineOptions(f.DataDir), [new OneGpu(d)], new SimStressRunner(), f.Plan, new FakeCpu(),
            f.Procs, f.Signals, f.Time);
        await e.StartAsync(default);
        return e;
    }

    [Fact]
    public async Task AcceptedWaiver_IsRememberedAcrossHelperRestarts()
    {
        await using var f = new EngineFixture();
        var first = new WaiverGpu(f.Gpu);
        await using (var e = await Start(f, first))
        {
            Assert.False((await e.HandleAsync(new IpcMessage(1, MessageTypes.GetState, null), default)).PayloadAs<HelperState>()!.Devices[0].IntelWaiverAccepted);
            await e.HandleAsync(IpcMessage.Create(2, MessageTypes.AcceptIntelWaiver, new DeviceRequest(first.Id)), default);
        }
        var second = new WaiverGpu(f.Gpu);
        await using var e2 = await Start(f, second);
        Assert.Equal(1, second.Accepts);   // Intel wants the waiver set each session; Redline re-sends the user's earlier yes
        Assert.True((await e2.HandleAsync(new IpcMessage(3, MessageTypes.GetState, null), default)).PayloadAs<HelperState>()!.Devices[0].IntelWaiverAccepted);
    }
}
