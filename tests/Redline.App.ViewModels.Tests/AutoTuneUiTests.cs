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

public class ProfileLabelTests
{
    [Fact]
    public async Task UntestedSendIt_SaysNotFullyTested_WithItsSettings()
    {
        var h = new FakeHelper();
        h.State = h.State with
        {
            Profiles = h.State.Profiles.Select(p => p.Id == "send-it" ? p with { Gpu = new TuneSettings(111, 225, 2300, null, null), StabilityTested = false } : p).ToList(),
        };
        var s = new ShellViewModel(h, FakeHelper.Inline) { Delay = HelperRecoveryTests.InstantThenNever(0) };
        await s.LoadAsync();
        var sub = s.Home.Profiles.Single(p => p.Id == "send-it").Subtitle;
        Assert.StartsWith("Not fully tested · ", sub);
        Assert.Contains("Core +225 MHz", sub);
    }
}

public class SignedFormatTests
{
    [Theory]
    [InlineData(-0.3, "+0")]
    [InlineData(0.2, "+0")]
    [InlineData(8.4, "+8")]
    [InlineData(-2.6, "−3")]
    public void Signed_NeverPrintsMinusPlus(double v, string expected) => Assert.Equal(expected, Numbers.Signed(v));
}

public class HeadlineTests
{
    [Fact]
    public async Task TuneOn_WithNoMeasuredGain_NeverSaysFactory()
    {
        var h = new FakeHelper(activeProfile: "send-it", active: new TuneSettings(null, 225, 1200, null, null));
        h.State = h.State with
        {
            Profiles = h.State.Profiles.Select(p => p.Id == "send-it"
                ? p with { Gpu = new TuneSettings(null, 225, 1200, null, null), Measured = new Redline.Core.Stats.BeforeAfter(34000, 30200, 49, 53, 273, 265, 33, 33) }
                : p).ToList(),
        };
        var s = new ShellViewModel(h, FakeHelper.Inline) { Delay = HelperRecoveryTests.InstantThenNever(0) };
        await s.LoadAsync();
        Assert.DoesNotContain("factory settings", s.Home.Headline);
        Assert.Contains("tune is on", s.Home.Headline, StringComparison.OrdinalIgnoreCase);
    }
}
