using Redline.Core.Storage;
using Redline.Ipc;

namespace Redline.App.ViewModels;

public enum CloseAction { Exit, MinimizeToTray }
public enum QuitChoice { BackToFactory, KeepTune }

public sealed record TrayMenuItem(string ProfileId, string Label, bool Checked);
public sealed record HotkeyBinding(string ProfileId, string ProfileName, string Gesture);
public readonly record struct HotkeyChord(uint Modifiers, uint Key);

/// <summary>Keyboard shortcuts for profiles. Always needs a modifier so a shortcut can't swallow normal typing.</summary>
public static class Hotkey
{
    // Win32 RegisterHotKey modifier flags.
    public const uint Alt = 0x1, Control = 0x2, Shift = 0x4, Win = 0x8;

    public static bool TryParse(string? text, out HotkeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        uint mods = 0, key = 0;
        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim().ToUpperInvariant();
            switch (part)
            {
                case "CTRL" or "CONTROL": mods |= Control; break;
                case "ALT": mods |= Alt; break;
                case "SHIFT": mods |= Shift; break;
                case "WIN" or "WINDOWS": mods |= Win; break;
                default:
                    if (key != 0 || KeyCode(part) is not { } k) return false;
                    key = k;
                    break;
            }
        }
        if (mods == 0 || key == 0) return false;
        chord = new HotkeyChord(mods, key);
        return true;
    }

    static uint? KeyCode(string part) => part switch
    {
        { Length: 1 } when part[0] is >= 'A' and <= 'Z' or >= '0' and <= '9' => part[0],
        _ when part.Length is 2 or 3 && part[0] == 'F' && int.TryParse(part[1..], out var n) && n is >= 1 and <= 24 => (uint)(0x70 + n - 1),
        _ => null,
    };

    public static string Format(HotkeyChord c)
    {
        var parts = new List<string>();
        if ((c.Modifiers & Control) != 0) parts.Add("Ctrl");
        if ((c.Modifiers & Alt) != 0) parts.Add("Alt");
        if ((c.Modifiers & Shift) != 0) parts.Add("Shift");
        if ((c.Modifiers & Win) != 0) parts.Add("Win");
        parts.Add(c.Key is >= 0x70 and <= 0x87 ? $"F{c.Key - 0x70 + 1}" : ((char)c.Key).ToString());
        return string.Join('+', parts);
    }
}

/// <summary>Tray menu, profile hotkeys and what closing/quitting does (spec §6.10).</summary>
public sealed class TrayViewModel(ShellViewModel shell) : ObservableObject
{
    public QuitChoice DefaultQuitChoice => QuitChoice.BackToFactory;

    /// <summary>True when any card is running something other than factory settings.</summary>
    public bool IsTuned => shell.State?.Devices.Any(d => !d.Active.IsFactory) ?? false;

    /// <summary>Closing the window with a tune active keeps Redline in the tray; at factory it just exits.</summary>
    public CloseAction OnWindowClose() => IsTuned ? CloseAction.MinimizeToTray : CloseAction.Exit;

    public async Task QuitAsync(QuitChoice choice)
    {
        if (choice != QuitChoice.BackToFactory || shell.State is not { } s) return;
        foreach (var d in s.Devices.Where(d => !d.Active.IsFactory))
            await shell.SendAsync(MessageTypes.ResetFactory, new DeviceRequest(d.Id));
    }

    public IReadOnlyList<TrayMenuItem> MenuItems =>
        shell.State is { } s
            ? s.Profiles.Select(p => new TrayMenuItem(p.Id, p.Name, shell.Device?.ActiveProfileId == p.Id)).ToList()
            : [];

    public async Task ApplyAsync(string profileId)
    {
        if (shell.Device is { } d) await shell.SendAsync(MessageTypes.ApplyProfile, new ApplyProfileRequest(d.Id, profileId));
    }

    public IReadOnlyList<HotkeyBinding> Hotkeys =>
        shell.State is { } s
            ? s.Settings.Hotkeys
                .Select(kv => (kv, p: s.Profiles.FirstOrDefault(p => p.Id == kv.Key)))
                .Where(x => x.p is not null)
                .Select(x => new HotkeyBinding(x.kv.Key, x.p!.Name, x.kv.Value)).ToList()
            : [];

    /// <summary>Assigns (or with null, clears) a profile's shortcut. Rejects invalid shortcuts and ones another profile already uses.</summary>
    public async Task<bool> SetHotkeyAsync(string profileId, string? gesture)
    {
        if (shell.State is not { } s) return false;
        var map = new Dictionary<string, string>(s.Settings.Hotkeys);
        if (gesture is null) map.Remove(profileId);
        else
        {
            if (!Hotkey.TryParse(gesture, out var chord))
            {
                shell.LastError = "Shortcuts need Ctrl, Alt, Shift or Win plus a letter, number or F-key.";
                return false;
            }
            var text = Hotkey.Format(chord);
            if (map.FirstOrDefault(kv => kv.Key != profileId && kv.Value == text) is { Key: { } other })
            {
                shell.LastError = $"{text} is already used by {s.Profiles.FirstOrDefault(p => p.Id == other)?.Name ?? "another profile"}.";
                return false;
            }
            map[profileId] = text;
        }
        await shell.UpdateSettingsAsync(st => st with { Hotkeys = map });
        OnStateChanged();
        return true;
    }

    public Task OnHotkeyAsync(string profileId) => ApplyAsync(profileId);

    /// <summary>Raised when the menu or shortcuts change (the app rebuilds the tray menu and re-registers keys).</summary>
    public event Action? Changed;

    internal void OnStateChanged()
    {
        OnPropertyChanged(nameof(IsTuned));
        OnPropertyChanged(nameof(MenuItems));
        OnPropertyChanged(nameof(Hotkeys));
        Changed?.Invoke();
    }
}
