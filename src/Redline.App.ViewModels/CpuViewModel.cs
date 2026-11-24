using System.Collections.ObjectModel;
using Redline.Core.Cpu;
using Redline.Core.Storage;
using Redline.Ipc;

namespace Redline.App.ViewModels;

/// <summary>CPU page: Windows boost settings per profile, and the BIOS tuning guide for this CPU.</summary>
public sealed class CpuViewModel : ObservableObject
{
    readonly ShellViewModel _shell;
    string? _profileId = Profile.WhisperId;
    CpuBoostMode? _boost;
    int _maxState = 100;

    public CpuViewModel(ShellViewModel shell)
    {
        _shell = shell;
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => _profileId is not null && _profileId != Profile.FactoryId);
    }

    public AsyncRelayCommand SaveCommand { get; }
    public ObservableCollection<ProfileChoice> Profiles { get; } = new();
    public IReadOnlyList<CpuBoostMode> BoostModes { get; } = Enum.GetValues<CpuBoostMode>();

    public static string Describe(CpuBoostMode m) => m switch
    {
        CpuBoostMode.Disabled => "Off — coolest and quietest",
        CpuBoostMode.Enabled => "On — Windows default",
        CpuBoostMode.Aggressive => "Aggressive — boosts hardest",
        CpuBoostMode.EfficientEnabled => "Efficient — boosts when it's worth it",
        _ => "Efficient aggressive",
    };

    public BiosGuide Guide => BiosGuides.For(_shell.State?.Cpu?.Name ?? "");
    public string CpuName => _shell.State?.Cpu?.Name ?? "CPU";

    public string? SelectedProfileId { get => _profileId; set { if (SetProperty(ref _profileId, value)) { LoadProfile(); SaveCommand.NotifyCanExecuteChanged(); } } }
    public CpuBoostMode? BoostMode { get => _boost; set => SetProperty(ref _boost, value); }
    public int MaxStatePct { get => _maxState; set => SetProperty(ref _maxState, Math.Clamp(value, 5, 100)); }

    internal void OnState()
    {
        OnPropertyChanged(nameof(Guide)); OnPropertyChanged(nameof(CpuName));
        if (_shell.State is not { } s) return;
        Profiles.Clear();
        foreach (var p in s.Profiles.Where(p => p.Id != Profile.FactoryId)) Profiles.Add(new ProfileChoice(p.Id, p.Name));
        LoadProfile();
    }

    void LoadProfile()
    {
        var p = _shell.State?.Profiles.FirstOrDefault(x => x.Id == _profileId);
        BoostMode = p?.Cpu?.BoostMode;
        MaxStatePct = p?.Cpu?.MaxProcessorStatePct ?? 100;
    }

    async Task SaveAsync()
    {
        if (_shell.State?.Profiles.FirstOrDefault(x => x.Id == _profileId) is not { } p) return;
        var cpu = new CpuBoostSettings(_boost, _maxState >= 100 ? null : _maxState);
        await _shell.SendAsync(MessageTypes.SaveProfile, new ProfileRequest(p with { Cpu = cpu.BoostMode is null && cpu.MaxProcessorStatePct is null ? null : cpu }));
    }
}
