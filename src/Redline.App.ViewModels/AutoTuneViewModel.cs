using System.Collections.ObjectModel;
using Redline.Core.AutoTune;
using Redline.Core.Storage;
using Redline.Ipc;

namespace Redline.App.ViewModels;

public sealed record StepItem(string Label, string Detail, bool Passed);

/// <summary>Auto-tune: pick a mode and target profile, watch progress, see the result.</summary>
public sealed class AutoTuneViewModel : ObservableObject
{
    readonly ShellViewModel _shell;
    AutoTuneMode _mode = AutoTuneMode.Performance;
    bool _allowPower, _running, _memoryOnly;
    string _target = Profile.SendItId;
    double _fraction;
    string _label = "", _remaining = "", _result = "";

    public AutoTuneViewModel(ShellViewModel shell)
    {
        _shell = shell;
        StartCommand = new AsyncRelayCommand(StartAsync, () => !Running && _shell.Device is { Caps.Ranges.Count: > 0 });
        CancelCommand = new AsyncRelayCommand(() => _shell.SendAsync(MessageTypes.CancelAutoTune, null), () => Running);
    }

    public AsyncRelayCommand StartCommand { get; }
    public AsyncRelayCommand CancelCommand { get; }
    public ObservableCollection<StepItem> Steps { get; } = new();

    public AutoTuneMode Mode
    {
        get => _mode;
        set { if (SetProperty(ref _mode, value)) Target = value == AutoTuneMode.Performance ? Profile.SendItId : Profile.WhisperId; }
    }

    /// <summary>Keep the current core clock and only look for the fastest memory setting (saved to Send it).</summary>
    public bool MemoryOnly
    {
        get => _memoryOnly && MemoryOnlyAvailable;
        set { if (SetProperty(ref _memoryOnly, value) && value) { _mode = AutoTuneMode.Performance; Target = Profile.SendItId; OnPropertyChanged(nameof(Mode)); } }
    }

    public bool MemoryOnlyAvailable => _shell.Device is { } d && d.Active.CoreOffsetMhz is > 0 && d.Caps.Supports(Redline.Core.Gpu.Control.MemoryOffset);

    public bool AllowPowerAbove100 { get => _allowPower; set => SetProperty(ref _allowPower, value); }
    public bool PowerOptInAvailable => _shell.State?.Settings.Safety.AllowPowerAbove100 == true && _mode == AutoTuneMode.Performance;
    public string Target { get => _target; set => SetProperty(ref _target, value); }
    public bool Running { get => _running; private set { if (SetProperty(ref _running, value)) { StartCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged(); } } }
    public double Fraction { get => _fraction; private set => SetProperty(ref _fraction, value); }
    public string CurrentLabel { get => _label; private set => SetProperty(ref _label, value); }
    public string Remaining { get => _remaining; private set => SetProperty(ref _remaining, value); }
    public string ResultText { get => _result; private set => SetProperty(ref _result, value); }

    internal void OnState()
    {
        Running = _shell.State?.AutoTuneRunning == true;
        OnPropertyChanged(nameof(PowerOptInAvailable));
        OnPropertyChanged(nameof(MemoryOnlyAvailable));
        OnPropertyChanged(nameof(MemoryOnly));
        StartCommand.NotifyCanExecuteChanged();
    }

    async Task StartAsync()
    {
        if (_shell.Device is not { } d) return;
        Steps.Clear(); ResultText = ""; Fraction = 0;
        var opts = MemoryOnly
            ? new AutoTuneOptions(AutoTuneMode.Performance, KeepFrom: d.Active)
            : new AutoTuneOptions(_mode, AllowPowerAbove100: _allowPower && PowerOptInAvailable);
        if (await _shell.SendAsync(MessageTypes.StartAutoTune, new StartAutoTuneRequest(d.Id, opts, _target)) is not null) Running = true;
    }

    internal void OnProgress(AutoTuneProgressEvent e)
    {
        Fraction = e.Progress.Fraction;
        CurrentLabel = e.Progress.Label;
        Remaining = e.Progress.EstimatedRemaining.TotalMinutes >= 1 ? $"About {Math.Ceiling(e.Progress.EstimatedRemaining.TotalMinutes):0} min left" : "Almost done";
        if (e.Result is not { } r) return;
        Running = false;
        Steps.Clear();
        foreach (var s in r.Log) Steps.Add(new StepItem(s.Label, s.Passed ? "Stable" : s.Result.DriverReset ? "Driver reset" : $"{s.Result.Errors} errors", s.Passed));
        ResultText = r.Outcome switch
        {
            AutoTuneOutcome.FailedAtBaseline => "Your card wasn't stable even at factory settings, so Redline stopped. Check cooling and drivers before tuning.",
            AutoTuneOutcome.Cancelled => "Stopped. Redline kept the last settings that passed.",
            _ when r.Best.IsFactory => "Your card is happiest at factory settings. Nothing was changed.",
            _ when _mode == AutoTuneMode.Efficiency => $"Done. Saving about {r.BaselinePowerW - r.FinalPowerW:0} W at {(r.FinalScore / r.BaselineScore - 1) * 100:+0.0;−0.0}% performance.",
            _ => $"Done. About {(r.FinalScore / r.BaselineScore - 1) * 100:+0;−0}% faster than factory, saved to your profile.",
        };
    }
}
