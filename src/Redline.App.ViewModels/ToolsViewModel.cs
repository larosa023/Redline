using Redline.Core.Score;
using Redline.Core.Sharing;
using Redline.Core.Stats;
using Redline.Core.Storage;
using Redline.Ipc;

namespace Redline.App.ViewModels;

/// <summary>Profile tools: before/after check, chip score, share codes.</summary>
public sealed class ToolsViewModel : ObservableObject
{
    readonly ShellViewModel _shell;
    string? _profileId = Profile.SendItId;
    string _import = "", _message = "";
    bool _busy;

    public ToolsViewModel(ShellViewModel shell)
    {
        _shell = shell;
        BeforeAfterCommand = new AsyncRelayCommand(BeforeAfterAsync, () => !_busy && Selected is { Gpu.IsFactory: false });
        ImportCommand = new AsyncRelayCommand(ImportAsync, () => _import.Trim().Length > 0);
    }

    public AsyncRelayCommand BeforeAfterCommand { get; }
    public AsyncRelayCommand ImportCommand { get; }

    Profile? Selected => _shell.State?.Profiles.FirstOrDefault(p => p.Id == _profileId);
    string GpuName => _shell.Device?.Name ?? "";

    public string? SelectedProfileId
    {
        get => _profileId;
        set { if (SetProperty(ref _profileId, value)) Refresh(); }
    }

    public string ImportCode { get => _import; set { if (SetProperty(ref _import, value)) ImportCommand.NotifyCanExecuteChanged(); } }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public bool Busy { get => _busy; private set { if (SetProperty(ref _busy, value)) BeforeAfterCommand.NotifyCanExecuteChanged(); } }

    public string ShareCode => Selected is { } p ? Core.Sharing.ShareCode.Encode(p, GpuName) : "";
    public ChipScoreResult? Score => Selected is { StabilityTested: true } p && p.Id != Profile.FactoryId ? ChipScore.Compute(GpuName, p.Gpu) : null;
    public string ScoreText => Score is { } s ? $"{s.Score} / 100 · {s.Tier}" : "Run auto-tune on this profile to get a chip score.";

    public static string Words(BeforeAfter b) =>
        $"{b.PerfChangePct:+0;−0}% performance · {b.TempChangeC:+0;−0} °C · {b.PowerChangeW:+0;−0} W";

    internal void OnState() => Refresh();

    void Refresh()
    {
        OnPropertyChanged(nameof(ShareCode)); OnPropertyChanged(nameof(Score)); OnPropertyChanged(nameof(ScoreText));
        BeforeAfterCommand.NotifyCanExecuteChanged();
    }

    async Task BeforeAfterAsync()
    {
        if (_shell.Device is not { } d || _profileId is null) return;
        Message = "Testing for about a minute: 30 seconds at factory, then 30 seconds with your tune…";
        if (await _shell.SendAsync(MessageTypes.RunBeforeAfter, new BeforeAfterRequest(d.Id, _profileId)) is not null) Busy = true;
        else Message = _shell.LastError ?? "";
    }

    internal void OnBeforeAfter(BeforeAfterResultEvent e)
    {
        Busy = false;
        Message = e.Result is { } r ? Words(r) : e.Error ?? "The check didn't finish.";
    }

    async Task ImportAsync()
    {
        var r = Core.Sharing.ShareCode.Decode(_import, GpuName);
        if (r.Error is not null || r.Profile is null)
        {
            Message = r.Error == ShareCodeError.UnsupportedVersion ? "That code is from a newer version of Redline." : "That doesn't look like a Redline code.";
            return;
        }
        if (await _shell.SendAsync(MessageTypes.ImportProfile, new ImportProfileRequest(r.Profile)) is null) return;
        Message = r.ModelMismatch
            ? $"Added \"{r.Profile.Name}\". It was made on a {r.GpuModel}, so Redline kept it inside your card's safe range. Test it before relying on it."
            : $"Added \"{r.Profile.Name}\". Run a before/after check or auto-tune to confirm it's stable on your card.";
        ImportCode = "";
    }
}
