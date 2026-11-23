using System.Collections.ObjectModel;
using Redline.Core.Fans;
using Redline.Core.Gpu;
using Redline.Core.Safety;
using Redline.Ipc;

namespace Redline.App.ViewModels;

public sealed class FanPointViewModel(int temp, int pct, Action changed) : ObservableObject
{
    int _t = temp, _p = pct;
    public int TempC { get => _t; set { if (SetProperty(ref _t, value)) changed(); } }
    public int Pct { get => _p; set { if (SetProperty(ref _p, value)) changed(); } }
}

/// <summary>Drag-to-edit fan curve. Applying stages it like any other change (keep-or-revert).</summary>
public sealed class FansViewModel : ObservableObject
{
    readonly ShellViewModel _shell;
    bool _custom;

    public FansViewModel(ShellViewModel shell)
    {
        _shell = shell;
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => CanControl && Errors.Count == 0 && _shell.Tune.PendingToken is null);
        AddPointCommand = new RelayCommand(AddPoint, () => Points.Count < 8);
        RemovePointCommand = new RelayCommand(() => { if (Points.Count > 2) { Points.RemoveAt(Points.Count - 2); Changed(); } });
        ResetCommand = new RelayCommand(() => Load(FanCurve.Default));
    }

    public ObservableCollection<FanPointViewModel> Points { get; } = new();
    public ObservableCollection<string> Errors { get; } = new();
    public AsyncRelayCommand ApplyCommand { get; }
    public RelayCommand AddPointCommand { get; }
    public RelayCommand RemovePointCommand { get; }
    public RelayCommand ResetCommand { get; }

    public bool CanControl => _shell.Device?.Caps.HasFanCurve == true;
    public int MinPct => _shell.Device?.Caps.Range(Control.FanSpeed)?.Min ?? 0;
    public bool UseCustomCurve { get => _custom; set { if (SetProperty(ref _custom, value)) Changed(); } }

    public FanCurve Curve => new(Points.Select(p => new FanPoint(p.TempC, p.Pct)).ToList());

    internal void OnState()
    {
        OnPropertyChanged(nameof(CanControl)); OnPropertyChanged(nameof(MinPct));
        var active = _shell.Device?.Active.Fan;
        _custom = active is not null;
        OnPropertyChanged(nameof(UseCustomCurve));
        Load(active ?? FanCurve.Default);
    }

    void Load(FanCurve c)
    {
        Points.Clear();
        foreach (var p in c.Points) Points.Add(new FanPointViewModel(p.TempC, p.Pct, Changed));
        Changed();
    }

    void AddPoint()
    {
        var last = Points[^1];
        var prev = Points.Count > 1 ? Points[^2] : last;
        Points.Insert(Points.Count - 1, new FanPointViewModel((prev.TempC + last.TempC) / 2, (prev.Pct + last.Pct) / 2, Changed));
        Changed();
    }

    void Changed()
    {
        Errors.Clear();
        if (_custom) foreach (var e in Curve.Validate(MinPct)) Errors.Add(e);
        ApplyCommand.NotifyCanExecuteChanged();
        AddPointCommand.NotifyCanExecuteChanged();
    }

    async Task ApplyAsync()
    {
        if (_shell.Device is not { } d) return;
        var s = d.Active with { Fan = _custom ? Curve : null };
        var safety = _shell.State?.Settings.Safety ?? SafetyOptions.Default;
        await _shell.SendAsync(MessageTypes.StageApply, new StageApplyRequest(d.Id, s, safety));
    }
}
