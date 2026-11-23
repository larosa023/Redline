using Redline.Core.Games;
using Xunit;

namespace Redline.Core.Tests.Games;

public class ProfileSwitcherTests
{
    static ProfileSwitcher New() => new([new GameRule("Cyberpunk2077.exe", "send-it"), new GameRule("eldenring.exe", "whisper")], "factory");

    [Fact]
    public void GameStart_SwitchesToRuleProfile()
    {
        var s = New();
        Assert.Equal("send-it", s.OnProcessStarted("Cyberpunk2077.exe"));
        Assert.Equal("send-it", s.ActiveProfileId);
    }

    [Fact]
    public void CaseInsensitive() => Assert.Equal("send-it", New().OnProcessStarted("CYBERPUNK2077.EXE"));

    [Fact]
    public void GameStop_ReturnsToBase()
    {
        var s = New();
        s.OnProcessStarted("Cyberpunk2077.exe");
        Assert.Equal("factory", s.OnProcessStopped("Cyberpunk2077.exe"));
        Assert.Equal("factory", s.ActiveProfileId);
    }

    [Fact]
    public void TwoGames_LastStartedWins_StopOne_SwitchesToOther()
    {
        var s = New();
        s.OnProcessStarted("Cyberpunk2077.exe");
        Assert.Equal("whisper", s.OnProcessStarted("eldenring.exe"));
        Assert.Equal("send-it", s.OnProcessStopped("eldenring.exe"));
    }

    [Fact]
    public void UnrelatedProcess_NoChange()
    {
        var s = New();
        Assert.Null(s.OnProcessStarted("notepad.exe"));
        Assert.Null(s.OnProcessStopped("notepad.exe"));
        Assert.Equal("factory", s.ActiveProfileId);
    }

    [Fact]
    public void SetBase_WhileNoGame_ReturnsNewBase()
    {
        var s = New();
        Assert.Equal("whisper", s.SetBase("whisper"));
        s.OnProcessStarted("Cyberpunk2077.exe");
        Assert.Null(s.SetBase("factory"));         // game still decides
        Assert.Equal("factory", s.OnProcessStopped("Cyberpunk2077.exe"));
    }

    [Fact]
    public void SameGameTwice_StopOneInstance_StaysOnGameProfile()
    {
        var s = New();
        s.OnProcessStarted("Cyberpunk2077.exe");
        s.OnProcessStarted("Cyberpunk2077.exe");
        Assert.Null(s.OnProcessStopped("Cyberpunk2077.exe"));
        Assert.Equal("factory", s.OnProcessStopped("Cyberpunk2077.exe"));
    }
}
