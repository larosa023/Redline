using Redline.App.ViewModels;
using Redline.Core.Gpu;
using Redline.Ipc;
using Xunit;

namespace Redline.App.ViewModels.Tests;

public class CloseBehaviorTests
{
    static async Task<ShellViewModel> Shell(FakeHelper h)
    {
        var s = new ShellViewModel(h, FakeHelper.Inline) { Delay = HelperRecoveryTests.InstantThenNever(0) };
        await s.LoadAsync();
        return s;
    }

    static FakeHelper Tuned() => new(activeProfile: "send-it", active: TuneSettings.Factory with { CoreOffsetMhz = 150 });

    [Fact]
    public async Task CloseWhileTuned_MinimizesToTray() =>
        Assert.Equal(CloseAction.MinimizeToTray, (await Shell(Tuned())).Tray.OnWindowClose());

    [Fact]
    public async Task CloseAtFactory_Exits() =>
        Assert.Equal(CloseAction.Exit, (await Shell(new FakeHelper())).Tray.OnWindowClose());

    [Fact]
    public async Task Quit_DefaultsToBackToFactory()
    {
        var h = Tuned();
        var s = await Shell(h);
        Assert.Equal(QuitChoice.BackToFactory, s.Tray.DefaultQuitChoice);
        await s.Tray.QuitAsync(QuitChoice.BackToFactory);
        Assert.Contains(h.Sent, m => m.Type == MessageTypes.ResetFactory && ((DeviceRequest)m.Payload!).DeviceId == "gpu0");
    }

    [Fact]
    public async Task Quit_KeepTune_ChangesNothing()
    {
        var h = Tuned();
        var s = await Shell(h);
        h.Sent.Clear();
        await s.Tray.QuitAsync(QuitChoice.KeepTune);
        Assert.Empty(h.Sent);
    }

    [Fact]
    public async Task TrayMenu_ListsProfiles_ChecksActive()
    {
        var s = await Shell(Tuned());
        var items = s.Tray.MenuItems;
        Assert.Equal(["send-it", "whisper", "factory"], items.Select(i => i.ProfileId).ToArray());
        Assert.True(items.Single(i => i.ProfileId == "send-it").Checked);
        Assert.False(items.Single(i => i.ProfileId == "factory").Checked);
    }

    [Fact]
    public async Task TrayPick_AppliesProfile()
    {
        var h = new FakeHelper();
        var s = await Shell(h);
        await s.Tray.ApplyAsync("whisper");
        Assert.Contains(h.Sent, m => m.Type == MessageTypes.ApplyProfile && ((ApplyProfileRequest)m.Payload!).ProfileId == "whisper");
    }

    [Theory]
    [InlineData("Ctrl+Alt+1", Hotkey.Control | Hotkey.Alt, 0x31)]
    [InlineData("ctrl + shift + f9", Hotkey.Control | Hotkey.Shift, 0x78)]
    [InlineData("Win+Alt+R", Hotkey.Win | Hotkey.Alt, 0x52)]
    public void Hotkey_Parses(string text, uint mods, uint vk)
    {
        Assert.True(Hotkey.TryParse(text, out var chord));
        Assert.Equal(new HotkeyChord(mods, vk), chord);
    }

    [Theory]
    [InlineData("1")]           // no modifier: would steal normal typing
    [InlineData("Ctrl+Alt")]    // no key
    [InlineData("Ctrl+Banana")]
    [InlineData("")]
    public void Hotkey_RejectsUnsafeOrInvalid(string text) => Assert.False(Hotkey.TryParse(text, out _));

    [Fact]
    public void Hotkey_FormatsCanonically()
    {
        Assert.True(Hotkey.TryParse("shift+ctrl+f9", out var c));
        Assert.Equal("Ctrl+Shift+F9", Hotkey.Format(c));
    }

    [Fact]
    public async Task SetHotkey_SavesToSettings()
    {
        var h = new FakeHelper();
        var s = await Shell(h);
        Assert.True(await s.Tray.SetHotkeyAsync("send-it", "ctrl+alt+1"));
        Assert.Equal("Ctrl+Alt+1", h.State.Settings.Hotkeys["send-it"]);
        Assert.Contains(s.Tray.Hotkeys, b => b.ProfileId == "send-it" && b.Gesture == "Ctrl+Alt+1");
        Assert.True(await s.Tray.SetHotkeyAsync("send-it", null));
        Assert.False(h.State.Settings.Hotkeys.ContainsKey("send-it"));
    }

    [Fact]
    public async Task SetHotkey_DuplicateRejected()
    {
        var h = new FakeHelper();
        var s = await Shell(h);
        Assert.True(await s.Tray.SetHotkeyAsync("send-it", "Ctrl+Alt+1"));
        Assert.False(await s.Tray.SetHotkeyAsync("whisper", "ctrl+alt+1"));
        Assert.False(h.State.Settings.Hotkeys.ContainsKey("whisper"));
        Assert.NotNull(s.LastError);
    }

    [Fact]
    public async Task Hotkey_Pressed_AppliesItsProfile()
    {
        var h = new FakeHelper();
        var s = await Shell(h);
        await s.Tray.SetHotkeyAsync("whisper", "Ctrl+Alt+2");
        await s.Tray.OnHotkeyAsync("whisper");
        Assert.Contains(h.Sent, m => m.Type == MessageTypes.ApplyProfile && ((ApplyProfileRequest)m.Payload!).ProfileId == "whisper");
    }
}
