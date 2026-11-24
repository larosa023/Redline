using System.Collections.ObjectModel;
using Redline.Core.Games;
using Redline.Core.Storage;

namespace Redline.App.ViewModels;

public sealed class GameRuleItem(string exe, string profileName, Func<Task> remove)
{
    public string ExeName { get; } = exe;
    public string ProfileName { get; } = profileName;
    public AsyncRelayCommand RemoveCommand { get; } = new(remove);
}

public sealed record ProfileChoice(string Id, string Name);

/// <summary>Games page: when a chosen game starts, its profile switches on; when it closes, your usual profile comes back.</summary>
public sealed class GamesViewModel : ObservableObject
{
    readonly ShellViewModel _shell;
    string _newExe = "";
    string? _newProfile = Profile.SendItId;

    public GamesViewModel(ShellViewModel shell)
    {
        _shell = shell;
        AddCommand = new AsyncRelayCommand(AddAsync, () => Normalize(_newExe) is { Length: > 4 } && _newProfile is not null);
    }

    public Func<IReadOnlyList<string>> RunningApps { get; set; } = () => [];
    public ObservableCollection<GameRuleItem> Rules { get; } = new();
    public ObservableCollection<ProfileChoice> Profiles { get; } = new();
    public AsyncRelayCommand AddCommand { get; }

    public string NewExe { get => _newExe; set { if (SetProperty(ref _newExe, value)) AddCommand.NotifyCanExecuteChanged(); } }
    public string? NewProfileId { get => _newProfile; set { if (SetProperty(ref _newProfile, value)) AddCommand.NotifyCanExecuteChanged(); } }

    static string Normalize(string exe)
    {
        var e = Path.GetFileName(exe.Trim().Trim('"'));
        return e.Length == 0 ? "" : e.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? e : e + ".exe";
    }

    internal void OnState()
    {
        if (_shell.State is not { } s) return;
        Profiles.Clear();
        foreach (var p in s.Profiles) Profiles.Add(new ProfileChoice(p.Id, p.Name));
        Rules.Clear();
        foreach (var r in s.Settings.GameRules)
            Rules.Add(new GameRuleItem(r.ExeName, s.Profiles.FirstOrDefault(p => p.Id == r.ProfileId)?.Name ?? r.ProfileId, () => RemoveAsync(r)));
    }

    async Task AddAsync()
    {
        var exe = Normalize(_newExe);
        await _shell.UpdateSettingsAsync(s => s.GameRules.Any(r => string.Equals(r.ExeName, exe, StringComparison.OrdinalIgnoreCase))
            ? s
            : s with { GameRules = [.. s.GameRules, new GameRule(exe, _newProfile!)] });
        NewExe = "";
    }

    Task RemoveAsync(GameRule rule) => _shell.UpdateSettingsAsync(s => s with { GameRules = s.GameRules.Where(r => r != rule).ToList() });
}
