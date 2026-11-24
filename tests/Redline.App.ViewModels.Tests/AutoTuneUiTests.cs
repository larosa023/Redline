using Redline.App.ViewModels;
using Redline.Core.AutoTune;
using Redline.Core.Gpu;
using Redline.Ipc;
using Xunit;

namespace Redline.App.ViewModels.Tests;

public class AutoTuneUiTests
{
    static async Task<(ShellViewModel, FakeHelper)> Shell(TuneSettings active, bool running = false)
    {
        var h = new FakeHelper(activeProfile: active.IsFactory ? "factory" : null, active: active);
        h.State = h.State with { AutoTuneRunning = running };
        var s = new ShellViewModel(h, FakeHelper.Inline) { Delay = HelperRecoveryTests.InstantThenNever(0) };
        await s.LoadAsync();
        return (s, h);
    }

    [Fact]
    public async Task Status_WhileAutoTuneRuns_SaysWhatItsTesting()
    {
        var (s, h) = await Shell(TuneSettings.Factory, running: true);
        h.Raise(MessageTypes.AutoTuneProgress, new AutoTuneProgressEvent("gpu0",
            new AutoTuneProgress(0.9, "Memory +2400 MHz", new TuneSettings(null, 225, 2400, null, null), TimeSpan.FromMinutes(3)), null));
        Assert.Equal("Auto-tune is testing · Memory +2400 MHz", s.Status);
    }

    [Fact]
    public async Task MemoryOnly_OfferedWhenCoreIsTuned_AndSendsIt()
    {
        var (s, h) = await Shell(new TuneSettings(null, 225, 2300, null, null));
        Assert.True(s.AutoTune.MemoryOnlyAvailable);
        s.AutoTune.MemoryOnly = true;
        await s.AutoTune.StartCommand.ExecuteAsync();
        var req = (StartAutoTuneRequest)h.Sent.Single(m => m.Type == MessageTypes.StartAutoTune).Payload!;
        Assert.True(req.Options.MemoryOnly);
        Assert.Equal(AutoTuneMode.Performance, req.Options.Mode);
        Assert.Equal("send-it", req.TargetProfileId);
    }

    [Fact]
    public async Task MemoryOnly_NotOfferedAtFactory()
    {
        var (s, _) = await Shell(TuneSettings.Factory);
        Assert.False(s.AutoTune.MemoryOnlyAvailable);
    }
}
