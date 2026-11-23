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
