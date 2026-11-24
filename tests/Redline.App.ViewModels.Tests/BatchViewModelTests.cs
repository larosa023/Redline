using Redline.App.ViewModels;
using Redline.Core.Cpu;
using Redline.Core.Games;
using Redline.Core.Gpu;
using Redline.Core.Sharing;
using Redline.Core.Stats;
using Redline.Core.Storage;
using Redline.Ipc;
using Xunit;

namespace Redline.App.ViewModels.Tests;

public class BatchViewModelTests
{
    static async Task<ShellViewModel> Shell(FakeHelper h)
    {
        var s = new ShellViewModel(h, FakeHelper.Inline);
        await s.LoadAsync();
        return s;
    }

    static Telemetry T(LimitReason limits = LimitReason.None) =>
        new(DateTimeOffset.UnixEpoch, 2835, 10501, 64, 77, 392, 98, 48, 1450, 6144) { VramTotalMb = 24576, PowerLimitW = 450, MemTempC = 68, Limits = limits };

    [Fact]
    public async Task Home_ShowsDetail()
    {
        var h = new FakeHelper(); var s = await Shell(h);
        h.Raise(MessageTypes.Telemetry, new TelemetryEvent("gpu0", T(LimitReason.Power),
            new CpuState("AMD Ryzen 9 7900X", 66, 16, 3300, 88, false) { CoreClocksMhz = [5100, 5050], CoreLoadsPct = [20, 10], RamUsedGb = 18.2, RamTotalGb = 63.2 }, false));
        Assert.Equal("10,501 MHz", s.Home.MemClockText);
        Assert.Equal("77°", s.Home.HotspotText);
        Assert.Equal("6.0 / 24.0 GB", s.Home.VramText);
        Assert.Equal("1,450 RPM", s.Home.FanRpmText);
        Assert.Equal("87% of limit", s.Home.PowerLimitText);
        Assert.Equal("Limited by power", s.Home.LimitText);
        Assert.Equal("18.2 / 63.2 GB", s.Home.RamText);
        Assert.Equal(2, s.Home.Cores.Count);
        Assert.Equal("5.1 GHz", s.Home.Cores[0].ClockText);
    }

    [Theory]
    [InlineData(LimitReason.None, "Nothing is holding it back")]
    [InlineData(LimitReason.Idle, "Idle")]
    [InlineData(LimitReason.Temperature, "Limited by temperature")]
    [InlineData(LimitReason.Power | LimitReason.Temperature, "Limited by power and temperature")]
    public void LimitText_Wording(LimitReason r, string expected) => Assert.Equal(expected, HomeViewModel.LimitWords(r));

    [Fact]
    public async Task Stats_LoadsSeries_WithSummary()
    {
        var h = new FakeHelper
        {
            Respond = (t, _) => t == MessageTypes.GetSeries
                ? IpcMessage.OkFor(0, new SeriesResponse([new StatSeries("GPU temp", "°C", [new(DateTimeOffset.UnixEpoch, 60), new(DateTimeOffset.UnixEpoch.AddSeconds(1), 70)], 60, 70, 65)]))
                : null,
        };
        var s = await Shell(h);
        s.Stats.Range = StatsRange.Hour;
        await s.Stats.RefreshAsync();
        var item = Assert.Single(s.Stats.Series);
        Assert.Equal("GPU temp", item.Metric);
        Assert.Equal("Min 60 · Avg 65 · Max 70 °C", item.Summary);
        Assert.Equal(StatsRange.Hour, ((SeriesRequest)h.Sent.Last(x => x.Type == MessageTypes.GetSeries).Payload!).Range);
    }

    [Fact]
    public async Task Games_AddAndRemoveRule_UpdatesSettings()
    {
        var h = new FakeHelper(); var s = await Shell(h);
        s.Games.NewExe = "Cyberpunk2077.exe";
        s.Games.NewProfileId = "send-it";
        await s.Games.AddCommand.ExecuteAsync();
        Assert.Equal("Cyberpunk2077.exe", Assert.Single(h.State.Settings.GameRules).ExeName);
        Assert.Equal("Send it", Assert.Single(s.Games.Rules).ProfileName);
        await s.Games.Rules[0].RemoveCommand.ExecuteAsync();
        Assert.Empty(h.State.Settings.GameRules);
    }

    [Fact]
    public async Task Games_RejectsEmptyOrDuplicate()
    {
        var h = new FakeHelper(); var s = await Shell(h);
        Assert.False(s.Games.AddCommand.CanExecute(null));
        s.Games.NewExe = "game"; s.Games.NewProfileId = "send-it";
        await s.Games.AddCommand.ExecuteAsync();
        Assert.Equal("game.exe", h.State.Settings.GameRules.Single().ExeName);
        s.Games.NewExe = "GAME.exe";
        await s.Games.AddCommand.ExecuteAsync();
        Assert.Single(h.State.Settings.GameRules);
    }

    [Fact]
    public async Task Cpu_SavesBoostToProfile_AndGuideMatchesCpu()
    {
        var h = new FakeHelper(); var s = await Shell(h);
        Assert.Contains("Ryzen 7000", s.Cpu.Guide.Title);
        s.Cpu.SelectedProfileId = "whisper";
        s.Cpu.BoostMode = CpuBoostMode.EfficientEnabled;
        await s.Cpu.SaveCommand.ExecuteAsync();
        var p = ((ProfileRequest)h.Sent.Last(x => x.Type == MessageTypes.SaveProfile).Payload!).Profile;
        Assert.Equal("whisper", p.Id);
        Assert.Equal(CpuBoostMode.EfficientEnabled, p.Cpu!.BoostMode);
    }

    [Fact]
    public async Task Tools_ShareCode_RoundTrips_AndImportSends()
    {
        var h = new FakeHelper(); var s = await Shell(h);
        s.Tools.SelectedProfileId = "send-it";
        var code = s.Tools.ShareCode;
        Assert.StartsWith("RL1.", code);
        s.Tools.ImportCode = code;
        await s.Tools.ImportCommand.ExecuteAsync();
        Assert.Contains(h.Sent, x => x.Type == MessageTypes.ImportProfile);
    }

    [Fact]
    public async Task Tools_BadImportCode_ShowsMessage()
    {
        var h = new FakeHelper(); var s = await Shell(h);
        s.Tools.ImportCode = "nonsense";
        await s.Tools.ImportCommand.ExecuteAsync();
        Assert.Equal("That doesn't look like a Redline code.", s.Tools.Message);
        Assert.DoesNotContain(h.Sent, x => x.Type == MessageTypes.ImportProfile);
    }

    [Fact]
    public async Task Tools_BeforeAfter_ResultText()
    {
        var h = new FakeHelper();
        h.State = h.State with { Profiles = h.State.Profiles.Select(p => p.Id == "send-it" ? p with { Gpu = TuneSettings.Factory with { CoreOffsetMhz = 150 } } : p).ToList() };
        var s = await Shell(h);
        s.Tools.SelectedProfileId = "send-it";
        await s.Tools.BeforeAfterCommand.ExecuteAsync();
        Assert.True(s.Tools.Busy);
        h.Raise(MessageTypes.BeforeAfterResult, new BeforeAfterResultEvent("send-it", new BeforeAfter(1000, 1070, 70, 64, 400, 390, 60, 55), null));
        Assert.False(s.Tools.Busy);
        Assert.Equal("+7% performance · −6 °C · −10 W", s.Tools.Message);
    }

    [Fact]
    public async Task Tools_ChipScore_ForTestedProfile()
    {
        var h = new FakeHelper();
        var profiles = h.State.Profiles.Select(p => p.Id == "send-it" ? p with { Gpu = TuneSettings.Factory with { CoreOffsetMhz = 150, MemoryOffsetMhz = 1000 }, StabilityTested = true } : p).ToList();
        h.State = h.State with { Profiles = profiles };
        var s = await Shell(h);
        s.Tools.SelectedProfileId = "send-it";
        Assert.Equal(50, s.Tools.Score!.Score);
        Assert.Equal("Good chip", s.Tools.Score.Tier);
    }

    [Fact]
    public async Task Overlay_ShowsLatestFps_AndHidesWhenStale()
    {
        var h = new FakeHelper();
        h.State = h.State with { Settings = new AppSettings { OverlayEnabled = true } };
        var s = await Shell(h);
        h.Raise(MessageTypes.FpsSample, new FpsSampleEvent("Game.exe", 143.6, 98.2));
        Assert.True(s.Overlay.Visible);
        Assert.Equal("144 FPS · 1% low 98", s.Overlay.FpsText);
        s.Overlay.Tick(DateTimeOffset.UtcNow.AddSeconds(10));
        Assert.False(s.Overlay.Visible);
    }
}
