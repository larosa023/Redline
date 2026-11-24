using Redline.Core.Gpu;
using Redline.Core.Safety;
using Redline.Core.Storage;
using Redline.Core.Games;
using Redline.Ipc;
using Xunit;

namespace Redline.Engine.Tests;

public class EngineTests : IAsyncLifetime
{
    EngineFixture F = new();
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await F.DisposeAsync();

    static readonly TuneSettings Tuned = TuneSettings.Factory with { CoreOffsetMhz = 150, MemoryOffsetMhz = 600 };

    async Task MakeTestedProfile(string id, TuneSettings gpu)
    {
        var store = new ProfileStore(F.DataDir);
        store.Update(store.Get(id) with { Gpu = gpu, StabilityTested = true, TestedAt = DateTimeOffset.UnixEpoch });
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Startup_AfterCrash_ResetsToFactory()
    {
        await F.Gpu.ApplyAsync(Tuned, default);
        var j = new SafeStartJournal(Path.Combine(F.DataDir, "journal.json"));
        j.MarkStarted(); j.MarkConfirmed("gpu0", Tuned);            // tune active, never shut down cleanly
        await F.StartAsync();
        Assert.True(F.Gpu.Current.IsFactory);
        Assert.Equal(StartupDecision.RecoverToFactory, (await F.State()).LastStartup);
    }

    [Fact]
    public async Task Startup_NeverAppliesWhenStartWithWindowsOff()
    {
        await MakeTestedProfile("send-it", Tuned);
        F.SaveSettings(new AppSettings { StartWithWindows = false, StartupProfileId = "send-it" });
        await F.StartAsync();
        F.Time.Advance(TimeSpan.FromSeconds(30));
        Assert.True(F.Gpu.Current.IsFactory);
    }

    [Fact]
    public async Task Startup_AppliesTestedProfileAfter15s()
    {
        await MakeTestedProfile("send-it", Tuned);
        F.SaveSettings(new AppSettings { StartWithWindows = true, StartupProfileId = "send-it" });
        await F.StartAsync();
        F.Time.Advance(TimeSpan.FromSeconds(14));
        Assert.True(F.Gpu.Current.IsFactory);
        F.Time.Advance(TimeSpan.FromSeconds(2));
        await F.Engine.IdleAsync();
        Assert.Equal(150, F.Gpu.Current.CoreOffsetMhz);
    }

    [Fact]
    public async Task Startup_SkipsUntestedProfile()
    {
        var store = new ProfileStore(F.DataDir);
        store.Update(store.Get("send-it") with { Gpu = Tuned, StabilityTested = false });
        F.SaveSettings(new AppSettings { StartWithWindows = true, StartupProfileId = "send-it" });
        await F.StartAsync();
        F.Time.Advance(TimeSpan.FromSeconds(30));
        await F.Engine.IdleAsync();
        Assert.True(F.Gpu.Current.IsFactory);
    }

    [Fact]
    public async Task Opening_And_GetState_AppliesNothing()
    {
        await F.StartAsync();
        var s = await F.State();
        Assert.True(F.Gpu.Current.IsFactory);
        var d = Assert.Single(s.Devices);
        Assert.True(d.Active.IsFactory);
        Assert.Equal("1.0.0", s.HelperVersion);
    }

    [Fact]
    public async Task StageApply_NoConfirm_RevertsAfter15s()
    {
        await F.StartAsync();
        var r = (await F.Send(MessageTypes.StageApply, new StageApplyRequest("gpu0", Tuned, EngineFixture.Safe))).PayloadAs<StageApplyResponse>()!;
        Assert.Equal(15, r.Seconds);
        Assert.Equal(150, F.Gpu.Current.CoreOffsetMhz);
        F.Time.Advance(TimeSpan.FromSeconds(16));
        await F.Engine.IdleAsync();
        Assert.True(F.Gpu.Current.IsFactory);
        Assert.Contains(F.Events, e => e.Type == MessageTypes.KeepOrRevertTick && e.PayloadAs<KeepOrRevertTickEvent>()!.Outcome == KeepOrRevertOutcome.Reverted);
    }

    [Fact]
    public async Task StageApply_Confirm_Keeps_AndPushesHistory()
    {
        await F.StartAsync();
        var r = (await F.Send(MessageTypes.StageApply, new StageApplyRequest("gpu0", Tuned, EngineFixture.Safe))).PayloadAs<StageApplyResponse>()!;
        await F.Send(MessageTypes.ConfirmKeep, new TokenRequest(r.Token));
        F.Time.Advance(TimeSpan.FromSeconds(30));
        await F.Engine.IdleAsync();
        Assert.Equal(150, F.Gpu.Current.CoreOffsetMhz);
        var h = (await F.Send(MessageTypes.GetHistory, new DeviceRequest("gpu0"))).PayloadAs<HistoryResponse>()!;
        Assert.Equal(150, Assert.Single(h.Entries).After.CoreOffsetMhz);
    }

    [Fact]
    public async Task StageApply_UnsafeValues_ClampedAndIssuesReturned()
    {
        await F.StartAsync();
        var r = (await F.Send(MessageTypes.StageApply, new StageApplyRequest("gpu0", TuneSettings.Factory with { CoreOffsetMhz = 480, PowerLimitPct = 120 }, EngineFixture.Safe)))
            .PayloadAs<StageApplyResponse>()!;
        Assert.Equal(300, r.Applied.CoreOffsetMhz);
        Assert.Equal(100, r.Applied.PowerLimitPct);
        Assert.Contains(SafetyIssue.ClampedToSafeRange, r.Issues);
        Assert.Contains(SafetyIssue.PowerAbove100NotAllowed, r.Issues);
    }

    [Fact]
    public async Task StageApply_WhileAnotherPending_Rejected()
    {
        await F.StartAsync();
        await F.Send(MessageTypes.StageApply, new StageApplyRequest("gpu0", Tuned, EngineFixture.Safe));
        var second = await F.Send(MessageTypes.StageApply, new StageApplyRequest("gpu0", Tuned, EngineFixture.Safe));
        Assert.Equal(MessageTypes.ErrorResponse, second.Type);
    }

    [Fact]
    public async Task ApplyProfile_NoCountdown()
    {
        await MakeTestedProfile("send-it", Tuned);
        await F.StartAsync();
        await F.Send(MessageTypes.ApplyProfile, new ApplyProfileRequest("gpu0", "send-it"));
        F.Time.Advance(TimeSpan.FromSeconds(30));
        await F.Engine.IdleAsync();
        Assert.Equal(150, F.Gpu.Current.CoreOffsetMhz);
        Assert.Equal("send-it", Assert.Single((await F.State()).Devices).ActiveProfileId);
    }

    [Fact]
    public async Task ApplyProfile_AppliesCpuBoost_FactoryRestores()
    {
        await F.StartAsync();
        await F.Send(MessageTypes.ApplyProfile, new ApplyProfileRequest("gpu0", "whisper"));
        Assert.Equal(Redline.Core.Cpu.CpuBoostMode.Disabled, F.Plan.Current.BoostMode);
        await F.Send(MessageTypes.ResetFactory, new DeviceRequest("gpu0"));
        Assert.Equal(Redline.Core.Cpu.CpuBoostMode.Aggressive, F.Plan.Current.BoostMode);
    }

    [Fact]
    public async Task Guard_OverCeiling_ScalesBack_EmitsGuardAction()
    {
        // sim temp = 30 + power/10 → power limit 133% of 400 W = 532 W → 83.2 °C
        await F.StartAsync();
        await F.Send(MessageTypes.UpdateSettings, new SettingsRequest(new AppSettings { Safety = new SafetyOptions(false, true, true) }));
        var r = (await F.Send(MessageTypes.StageApply, new StageApplyRequest("gpu0", Tuned with { PowerLimitPct = 133 }, new SafetyOptions(false, true, true))))
            .PayloadAs<StageApplyResponse>()!;
        await F.Send(MessageTypes.ConfirmKeep, new TokenRequest(r.Token));
        for (var i = 0; i < 6; i++) { F.Time.Advance(TimeSpan.FromSeconds(5)); await F.Engine.IdleAsync(); }
        Assert.Contains(F.Events, e => e.Type == MessageTypes.GuardAction);
        Assert.Equal(75, F.Gpu.Current.CoreOffsetMhz);
    }

    [Fact]
    public async Task DriverReset_WhileTuned_AppliesFactory()
    {
        await MakeTestedProfile("send-it", Tuned);
        await F.StartAsync();
        await F.Send(MessageTypes.ApplyProfile, new ApplyProfileRequest("gpu0", "send-it"));
        F.Signals.Reset();
        await F.Engine.IdleAsync();
        Assert.True(F.Gpu.Current.IsFactory);
        Assert.Contains(F.Events, e => e.Type == MessageTypes.Toast);
    }

    [Fact]
    public async Task DeviceLost_EngineRecovers()
    {
        await MakeTestedProfile("send-it", Tuned);
        await F.StartAsync();
        await F.Send(MessageTypes.ApplyProfile, new ApplyProfileRequest("gpu0", "send-it"));
        F.Gpu.SimulateLoss(true);
        await F.Gpu.ResetAsync(default);                         // driver reset wiped the tune
        for (var i = 0; i < 10; i++) { F.Time.Advance(TimeSpan.FromSeconds(5)); await F.Engine.IdleAsync(); }
        F.Gpu.SimulateLoss(false);
        F.Time.Advance(TimeSpan.FromSeconds(5)); await F.Engine.IdleAsync();
        Assert.Equal(150, F.Gpu.Current.CoreOffsetMhz);
    }

    [Fact]
    public async Task GameStart_SwitchesProfile()
    {
        await MakeTestedProfile("send-it", Tuned);
        F.SaveSettings(new AppSettings { GameRules = [new GameRule("Cyberpunk2077.exe", "send-it")] });
        await F.StartAsync();
        F.Procs.Start("Cyberpunk2077.exe");
        await F.Engine.IdleAsync();
        Assert.Equal(150, F.Gpu.Current.CoreOffsetMhz);
        F.Procs.Stop("Cyberpunk2077.exe");
        await F.Engine.IdleAsync();
        Assert.True(F.Gpu.Current.IsFactory);
    }

    [Fact]
    public async Task PollInterval_1sWithSubscriber_5sWithout()
    {
        await F.StartAsync();
        Assert.Equal(TimeSpan.FromSeconds(5), F.Engine.CurrentPollInterval);
        F.Engine.SubscriberCount = 1;
        Assert.Equal(TimeSpan.FromSeconds(1), F.Engine.CurrentPollInterval);
        F.Time.Advance(TimeSpan.FromSeconds(3)); await F.Engine.IdleAsync();
        Assert.True(F.Events.Count(e => e.Type == MessageTypes.Telemetry) >= 3);
    }

    [Fact]
    public async Task Quit_NoKeep_ResetsAndRestoresCpu()
    {
        await MakeTestedProfile("send-it", Tuned);
        await F.StartAsync();
        await F.Send(MessageTypes.ApplyProfile, new ApplyProfileRequest("gpu0", "whisper"));
        await F.Send(MessageTypes.ApplyProfile, new ApplyProfileRequest("gpu0", "send-it"));
        await F.Send(MessageTypes.Quit, new QuitRequest(false));
        Assert.True(F.Gpu.Current.IsFactory);
        Assert.Equal(Redline.Core.Cpu.CpuBoostMode.Aggressive, F.Plan.Current.BoostMode);
    }

    [Fact]
    public async Task AutoTune_UpdatesTargetProfile_AndMarksTested()
    {
        await F.StartAsync();
        await F.Send(MessageTypes.StartAutoTune, new StartAutoTuneRequest("gpu0", new Redline.Core.AutoTune.AutoTuneOptions(Redline.Core.AutoTune.AutoTuneMode.Performance), "send-it"));
        await F.Engine.AutoTuneTask!;
        await F.Engine.IdleAsync();
        var p = (await F.State()).Profiles.Single(x => x.Id == "send-it");
        Assert.True(p.StabilityTested);
        Assert.Equal(150, p.Gpu.CoreOffsetMhz);
        Assert.Equal(150, F.Gpu.Current.CoreOffsetMhz);
        Assert.Contains(F.Events, e => e.Type == MessageTypes.AutoTuneProgress && e.PayloadAs<AutoTuneProgressEvent>()!.Result is not null);
    }

    [Fact]
    public async Task UnknownDevice_ReturnsError()
    {
        await F.StartAsync();
        Assert.Equal(MessageTypes.ErrorResponse, (await F.Send(MessageTypes.ResetFactory, new DeviceRequest("nope"))).Type);
    }
}

// Full simulated auto-tunes are CPU-heavy; keep them from running alongside the timing-sensitive pipe tests.
[Collection("CpuHeavy")]
public class AutoTuneStopTests
{
    [Fact]
    public async Task BackToFactory_DuringAutoTune_EndsAtFactory()
    {
        await using var f = new EngineFixture();
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        f.Stress.OnRun = i => { if (i == 6) { reached.Set(); release.Wait(TimeSpan.FromSeconds(10)); } };
        await f.StartAsync();
        await f.Send(MessageTypes.StartAutoTune, new StartAutoTuneRequest("gpu0", new Redline.Core.AutoTune.AutoTuneOptions(Redline.Core.AutoTune.AutoTuneMode.Performance), "send-it"));
        Assert.True(reached.Wait(TimeSpan.FromSeconds(10)));
        Assert.False(f.Gpu.Current.IsFactory);                 // mid-test: a raised clock is on the card

        var reset = f.Send(MessageTypes.ResetFactory, new DeviceRequest("gpu0"));
        await Task.Delay(200);
        release.Set();                                         // the stress step finishes after the button press
        await reset;
        await f.Engine.AutoTuneTask!;
        await f.Engine.IdleAsync();

        Assert.True(f.Gpu.Current.IsFactory);
        Assert.True((await f.State()).Devices[0].Active.IsFactory);
    }

    [Fact]
    public async Task MemoryOnly_StartsFromWhatsOnTheCard_NotFromTheRequest()
    {
        await using var f = new EngineFixture();
        await f.StartAsync();
        await f.Send(MessageTypes.StageApply, new StageApplyRequest("gpu0", new TuneSettings(null, 150, 300, null, null), Redline.Core.Safety.SafetyOptions.Default));
        await f.Send(MessageTypes.ConfirmKeep, new DeviceRequest("gpu0"));
        var bogus = new Redline.Core.AutoTune.AutoTuneOptions(Redline.Core.AutoTune.AutoTuneMode.Performance, KeepFrom: new TuneSettings(null, 999, null, null, null));
        await f.Send(MessageTypes.StartAutoTune, new StartAutoTuneRequest("gpu0", bogus, "send-it"));
        await f.Engine.AutoTuneTask!;
        Assert.All(f.Stress.Tested.Skip(1), x => Assert.Equal(150, x.CoreOffsetMhz));
    }

    static Redline.Core.AutoTune.AutoTuneOptions PerfOpts => new(Redline.Core.AutoTune.AutoTuneMode.Performance);

    [Fact]
    public async Task StoppedEarly_SavesWhatPassed_ButNotAsFullyTested()
    {
        await using var f = new EngineFixture();
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        f.Stress.OnRun = i => { if (i == 6) { reached.Set(); release.Wait(TimeSpan.FromSeconds(10)); } };
        await f.StartAsync();
        await f.Send(MessageTypes.StartAutoTune, new StartAutoTuneRequest("gpu0", PerfOpts, "send-it"));
        Assert.True(reached.Wait(TimeSpan.FromSeconds(10)));
        await f.Send(MessageTypes.CancelAutoTune);
        release.Set();
        await f.Engine.AutoTuneTask!;

        var sendIt = (await f.State()).Profiles.Single(p => p.Id == "send-it");
        Assert.False(sendIt.Gpu.IsFactory);
        Assert.Equal(f.Gpu.Current.CoreOffsetMhz, sendIt.Gpu.CoreOffsetMhz);
        Assert.False(sendIt.StabilityTested);          // the 5-minute final check never ran
        Assert.Equal("send-it", (await f.State()).Devices[0].ActiveProfileId);
    }

    [Fact]
    public async Task HelperStop_MidTest_LeavesLastGood_NotTheStepUnderTest()
    {
        await using var f = new EngineFixture();
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        f.Stress.OnRun = i => { if (i == 6) { reached.Set(); release.Wait(TimeSpan.FromSeconds(10)); } };
        await f.StartAsync();
        await f.Send(MessageTypes.StartAutoTune, new StartAutoTuneRequest("gpu0", PerfOpts, "send-it"));
        Assert.True(reached.Wait(TimeSpan.FromSeconds(10)));
        var underTest = f.Gpu.Current.CoreOffsetMhz;

        _ = Task.Run(async () => { await Task.Delay(200); release.Set(); });
        await f.Engine.StopAsync(keepTune: true);       // the process exits right after this returns

        Assert.NotEqual(underTest, f.Gpu.Current.CoreOffsetMhz);
        Assert.True(f.Gpu.Current.CoreOffsetMhz < underTest);
    }

    [Fact]
    public async Task StateAfterAutoTune_SaysItIsNoLongerRunning()
    {
        await using var f = new EngineFixture();
        await f.StartAsync();
        await f.Send(MessageTypes.StartAutoTune, new StartAutoTuneRequest("gpu0", PerfOpts, "send-it"));
        await f.Engine.AutoTuneTask!;
        IpcMessage last;
        lock (f.Events) last = f.Events.Last(m => m.Type == MessageTypes.State);
        Assert.False(last.PayloadAs<HelperState>()!.AutoTuneRunning);
    }

    [Fact]
    public async Task MemoryOnly_DropsPowerAbove100_WhenThatIsNotAllowed()
    {
        await using var f = new EngineFixture();
        await f.Gpu.ApplyAsync(new TuneSettings(111, 150, null, null, null), default);   // left on the card from before
        await f.StartAsync();
        await f.Send(MessageTypes.StartAutoTune, new StartAutoTuneRequest("gpu0", PerfOpts with { KeepFrom = TuneSettings.Factory }, "send-it"));
        await f.Engine.AutoTuneTask!;
        Assert.All(f.Stress.Tested.Skip(1), x => Assert.True(x.PowerLimitPct is null or <= 100, $"tested at {x.PowerLimitPct}%"));
        var sendIt = (await f.State()).Profiles.Single(p => p.Id == "send-it");
        Assert.True(sendIt.Gpu.PowerLimitPct is null or <= 100);
    }

    [Fact]
    public async Task StoredProfile_AboveWhatSafetyAllows_IsTrimmedAtStart()
    {
        await using var f = new EngineFixture();
        var store = new Redline.Core.Storage.ProfileStore(f.DataDir);
        var sendIt = store.Get("send-it");
        store.Update(sendIt with
        {
            Gpu = new TuneSettings(111, 225, 1200, null, null), StabilityTested = true,
            Measured = new Redline.Core.Stats.BeforeAfter(100, 108, 60, 62, 300, 340, 40, 45),
        });
        await f.StartAsync();

        var p = (await f.State()).Profiles.Single(x => x.Id == "send-it");
        Assert.Null(p.Gpu.PowerLimitPct);          // 100% is the default, stored as "not changed"
        Assert.Equal(225, p.Gpu.CoreOffsetMhz);
        Assert.Null(p.Measured);                   // measured at 111%: no longer describes this profile
        Assert.True(p.StabilityTested);            // less power than tested is still tested
    }
}
