using Redline.App.ViewModels;
using Redline.Core.Gpu;
using Redline.Core.Safety;
using Redline.Core.Storage;
using Redline.Ipc;
using Xunit;

namespace Redline.App.ViewModels.Tests;

public class ViewModelTests
{
    static async Task<ShellViewModel> Shell(FakeHelper h)
    {
        var s = new ShellViewModel(h, FakeHelper.Inline);
        await s.LoadAsync();
        return s;
    }

    static readonly string[] Mutating = [MessageTypes.StageApply, MessageTypes.ApplyProfile, MessageTypes.ResetFactory, MessageTypes.StartAutoTune, MessageTypes.Undo];

    [Fact]
    public async Task Opening_SendsNoApply()
    {
        var h = new FakeHelper();
        var s = await Shell(h);
        _ = s.Home; _ = s.Tune; _ = s.Fans; _ = s.AutoTune;
        Assert.DoesNotContain(h.Sent, x => Mutating.Contains(x.Type));
        Assert.Contains(h.Sent, x => x.Type == MessageTypes.SubscribeTelemetry);
    }

    [Fact]
    public async Task SliderMove_StagesOnly()
    {
        var h = new FakeHelper();
        var s = await Shell(h);
        s.Tune.Slider(Control.CoreOffset)!.Value = 150;
        Assert.True(s.Tune.HasChanges);
        Assert.DoesNotContain(h.Sent, x => x.Type == MessageTypes.StageApply);
    }

    [Fact]
    public async Task ApplySummary_Format()
    {
        var s = await Shell(new FakeHelper());
        s.Tune.Slider(Control.CoreOffset)!.Value = 150;
        s.Tune.Slider(Control.MemoryOffset)!.Value = 600;
        Assert.Equal("Core +150 MHz · Memory +600 MHz", s.Tune.ApplySummary);
        s.Tune.Slider(Control.PowerLimit)!.Value = 90;
        Assert.Equal("Power 90% · Core +150 MHz · Memory +600 MHz", s.Tune.ApplySummary);
    }

    [Fact]
    public async Task Apply_ShowsCountdownFrom15_AndKeepConfirms()
    {
        var h = new FakeHelper();
        var s = await Shell(h);
        s.Tune.Slider(Control.CoreOffset)!.Value = 150;
        await s.Tune.ApplyCommand.ExecuteAsync();
        Assert.True(s.Tune.CountdownVisible);
        Assert.Equal(15, s.Tune.SecondsLeft);
        var token = ((StageApplyRequest)h.Sent.Last(x => x.Type == MessageTypes.StageApply).Payload!).Settings;
        Assert.Equal(150, token.CoreOffsetMhz);
        h.Raise(MessageTypes.KeepOrRevertTick, new KeepOrRevertTickEvent(s.Tune.PendingToken!.Value, 9, false, null));
        Assert.Equal(9, s.Tune.SecondsLeft);
        await s.Tune.KeepCommand.ExecuteAsync();
        Assert.Contains(h.Sent, x => x.Type == MessageTypes.ConfirmKeep);
        h.Raise(MessageTypes.KeepOrRevertTick, new KeepOrRevertTickEvent(s.Tune.PendingToken!.Value, 0, true, KeepOrRevertOutcome.Kept));
        Assert.False(s.Tune.CountdownVisible);
    }

    [Fact]
    public async Task PowerAbove100_16Pin_RequiresWarning()
    {
        var h = new FakeHelper(FakeHelper.Caps(sixteenPin: true));
        h.State = h.State with { Settings = new AppSettings { Safety = new SafetyOptions(false, true, false) } };
        var s = await Shell(h);
        s.Tune.Slider(Control.PowerLimit)!.Value = 110;
        Assert.True(s.Tune.NeedsConnectorWarning);
        Assert.False(s.Tune.ApplyCommand.CanExecute(null));
        s.Tune.AcknowledgeConnectorCommand.Execute(null);
        Assert.False(s.Tune.NeedsConnectorWarning);
        Assert.True(s.Tune.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task Sliders_UseSafeRange_UnlessAdvanced()
    {
        var s = await Shell(new FakeHelper());
        Assert.Equal(600, s.Tune.Slider(Control.CoreOffset)!.Max);
        Assert.Equal(100, s.Tune.Slider(Control.PowerLimit)!.Max);
        s.Tune.AdvancedEnabled = true;
        Assert.Equal(1000, s.Tune.Slider(Control.CoreOffset)!.Max);
    }

    [Fact]
    public async Task Status_FactoryText() => Assert.Equal("Factory, nothing applied", (await Shell(new FakeHelper())).Status);

    [Fact]
    public async Task Status_ProfileText() =>
        Assert.Equal("Send it is active", (await Shell(new FakeHelper(activeProfile: "send-it", active: TuneSettings.Factory with { CoreOffsetMhz = 150 }))).Status);

    [Fact]
    public async Task Status_CustomText() =>
        Assert.Equal("Custom tune is active · Core +15 MHz", (await Shell(new FakeHelper(activeProfile: null, active: TuneSettings.Factory with { CoreOffsetMhz = 15 }))).Status);

    [Fact]
    public async Task HelperMissing_ShowsRepair()
    {
        var s = new ShellViewModel(new FakeHelper { Available = false }, FakeHelper.Inline) { Delay = HelperRecoveryTests.InstantThenNever(0) };
        await s.LoadAsync();
        Assert.True(s.HelperMissing);
        var asked = false;
        s.Installer = () => { asked = true; return Task.FromResult(false); };
        await s.RepairCommand.ExecuteAsync();
        Assert.True(asked);
    }

    [Fact]
    public async Task Disconnect_ShowsHelperMissing()
    {
        var h = new FakeHelper();
        var s = await Shell(h);
        h.Drop();
        Assert.True(s.HelperMissing);
    }

    [Fact]
    public async Task Chip_Cruising_And_PersonalBest()
    {
        var h = new FakeHelper(activeProfile: "send-it", active: TuneSettings.Factory with { CoreOffsetMhz = 150 });
        h.State = h.State with { NewPersonalBest = true, PersonalBestMhz = 2850 };
        var s = await Shell(h);
        h.Raise(MessageTypes.Telemetry, new TelemetryEvent("gpu0", new Telemetry(DateTimeOffset.UnixEpoch, 2835, 10501, 64, null, 392, 98, 48, 1400, 6000), null, false));
        Assert.Contains("Cruising", s.Home.Chips);
        Assert.Contains("New personal best", s.Home.Chips);
        Assert.Equal(2835, s.Home.CoreClockMhz);
    }

    [Fact]
    public async Task Chip_RunningWarm_NearCeiling()
    {
        var h = new FakeHelper(activeProfile: "send-it", active: TuneSettings.Factory with { CoreOffsetMhz = 150 });
        var s = await Shell(h);
        h.Raise(MessageTypes.Telemetry, new TelemetryEvent("gpu0", new Telemetry(DateTimeOffset.UnixEpoch, 2835, 10501, 80, null, 392, 98, 48, 1400, 6000), null, false));
        Assert.Contains("Running warm", s.Home.Chips);
        Assert.DoesNotContain("Cruising", s.Home.Chips);
    }

    [Fact]
    public async Task UnsupportedControls_Hidden()
    {
        var s = await Shell(new FakeHelper(Capabilities.MonitorOnly));
        Assert.Empty(s.Tune.Sliders);
        Assert.False(s.Tune.CanTune);
        Assert.True(s.Home.HasGpu);
    }

    [Fact]
    public async Task Tach_RedlineStartsAt85Percent()
    {
        var s = await Shell(new FakeHelper());
        Assert.Equal(0.85, s.Home.TachRedlineFraction, 2);
    }

    [Fact]
    public async Task ApplyProfile_FromHome_SendsRequest()
    {
        var h = new FakeHelper();
        var s = await Shell(h);
        await s.Home.Profiles.Single(p => p.Id == "whisper").ApplyCommand.ExecuteAsync();
        var (type, payload) = h.Sent.Last();
        Assert.Equal(MessageTypes.ApplyProfile, type);
        Assert.Equal("whisper", ((ApplyProfileRequest)payload!).ProfileId);
    }

    [Fact]
    public async Task ErrorResponse_ShowsMessage_NoCrash()
    {
        var h = new FakeHelper { Respond = (t, _) => t == MessageTypes.StageApply ? IpcMessage.Error(0, "Keep or revert the current change first.") : null };
        var s = await Shell(h);
        s.Tune.Slider(Control.CoreOffset)!.Value = 150;
        await s.Tune.ApplyCommand.ExecuteAsync();
        Assert.Equal("Keep or revert the current change first.", s.LastError);
        Assert.False(s.Tune.CountdownVisible);
    }
}

public class ShellThemeTests
{
    [Fact]
    public async Task SetTheme_SendsUpdateSettings()
    {
        var h = new FakeHelper();
        var s = new ShellViewModel(h, FakeHelper.Inline);
        await s.LoadAsync();
        await s.SetThemeAsync("neon");
        Assert.Equal("neon", s.Theme);
        var (type, payload) = h.Sent.Last();
        Assert.Equal(MessageTypes.UpdateSettings, type);
        Assert.Equal("neon", ((SettingsRequest)payload!).Settings.Theme);
    }
}
