using System.Collections.ObjectModel;
using Redline.Core.Gpu;
using Redline.Core.Safety;
using Redline.Ipc;

namespace Redline.App.ViewModels;

public sealed class SliderViewModel(Control control, string label, string unit, Action changed) : ObservableObject
{
    int _min, _max, _step, _value, _default;
    public Control Control { get; } = control;
    public string Label { get; } = label;
    public string Unit { get; } = unit;
    public int Min { get => _min; set { if (SetProperty(ref _min, value)) OnPropertyChanged(nameof(MinD)); } }
    public int Max { get => _max; set { if (SetProperty(ref _max, value)) OnPropertyChanged(nameof(MaxD)); } }
    public int Step { get => _step; set { if (SetProperty(ref _step, value)) OnPropertyChanged(nameof(StepD)); } }
    public int Default { get => _default; set => SetProperty(ref _default, value); }

    public int Value
    {
        get => _value;
        set
        {
            var v = Math.Clamp(Step > 1 ? (int)Math.Round(value / (double)Step) * Step : value, Min, Math.Max(Min, Max));
            if (SetProperty(ref _value, v)) { OnPropertyChanged(nameof(Display)); OnPropertyChanged(nameof(ValueD)); changed(); }
        }
    }

    internal void SetSilently(int v) { _value = v; OnPropertyChanged(nameof(Value)); OnPropertyChanged(nameof(ValueD)); OnPropertyChanged(nameof(Display)); }

    public string Display => Control == Control.PowerLimit ? $"{Value}%" : $"{Value:+0;−0;0} {Unit}";

    // double-typed mirrors for UI sliders
    public double ValueD { get => Value; set => Value = (int)Math.Round(value); }
    public double MinD => Min;
    public double MaxD => Max;
    public double StepD => Step;
}

/// <summary>Manual tuning. Slider changes are staged; nothing reaches the card until Apply, then a 15-second keep-or-revert.</summary>
public sealed class TuneViewModel : ObservableObject
{
    readonly ShellViewModel _shell;
    bool _advanced, _ackLocal, _countdown;
    int _secondsLeft;
    Guid? _token;

    public TuneViewModel(ShellViewModel shell)
    {
        _shell = shell;
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => CanTune && HasChanges && !NeedsConnectorWarning && !CountdownVisible);
        KeepCommand = new AsyncRelayCommand(() => _token is { } t ? _shell.SendAsync(MessageTypes.ConfirmKeep, new TokenRequest(t)) : Task.CompletedTask);
        RevertCommand = new AsyncRelayCommand(() => _token is { } t ? _shell.SendAsync(MessageTypes.RevertNow, new TokenRequest(t)) : Task.CompletedTask);
        DiscardCommand = new RelayCommand(LoadFromActive);
        AcceptWaiverCommand = new AsyncRelayCommand(() => _shell.Device is { } dv ? _shell.SendAsync(MessageTypes.AcceptIntelWaiver, new DeviceRequest(dv.Id)) : Task.CompletedTask);
        ResetCommand = new AsyncRelayCommand(() => _shell.Device is { } d ? _shell.SendAsync(MessageTypes.ResetFactory, new DeviceRequest(d.Id)) : Task.CompletedTask);
        UndoCommand = new AsyncRelayCommand(() => _shell.Device is { } d ? _shell.SendAsync(MessageTypes.Undo, new DeviceRequest(d.Id)) : Task.CompletedTask);
        AcknowledgeConnectorCommand = new RelayCommand(() => { _ackLocal = true; Changed(); });
    }

    public ObservableCollection<SliderViewModel> Sliders { get; } = new();
    public AsyncRelayCommand ApplyCommand { get; }
    public AsyncRelayCommand KeepCommand { get; }
    public AsyncRelayCommand RevertCommand { get; }
    public RelayCommand DiscardCommand { get; }
    public AsyncRelayCommand ResetCommand { get; }
    public AsyncRelayCommand UndoCommand { get; }
    public RelayCommand AcknowledgeConnectorCommand { get; }

    public bool CanTune => Sliders.Count > 0;
    public SliderViewModel? Slider(Control c) => Sliders.FirstOrDefault(s => s.Control == c);

    public bool AdvancedEnabled
    {
        get => _advanced;
        set { if (SetProperty(ref _advanced, value)) RebuildRanges(); }
    }

    public bool CountdownVisible { get => _countdown; private set { if (SetProperty(ref _countdown, value)) Changed(); } }
    public int SecondsLeft { get => _secondsLeft; private set { if (SetProperty(ref _secondsLeft, value)) OnPropertyChanged(nameof(CountdownText)); } }
    public string CountdownText => Copy.KeepPrompt(SecondsLeft);
    public Guid? PendingToken => _token;

    SafetyOptions Safety => (_shell.State?.Settings.Safety ?? SafetyOptions.Default) is var s
        ? s with { Advanced = _advanced, ConnectorWarningAcknowledged = s.ConnectorWarningAcknowledged || _ackLocal }
        : SafetyOptions.Default;

    public TuneSettings Staged
    {
        get
        {
            var t = (_shell.Device?.Active ?? TuneSettings.Factory) with { PowerLimitPct = null, CoreOffsetMhz = null, MemoryOffsetMhz = null, VoltageOffsetMv = null };
            foreach (var s in Sliders) t = t.With(s.Control, s.Value == s.Default ? null : s.Value);
            return t;
        }
    }

    public bool HasChanges => _shell.Device is { } d && TuneSettings.ScalarControls.Any(c => Staged.Get(c) != d.Active.Get(c));

    public bool NeedsConnectorWarning =>
        _shell.Device is { Caps.Has16PinConnector: true } && (Staged.PowerLimitPct ?? 100) > 100 && !Safety.ConnectorWarningAcknowledged;

    public string ApplySummary => Summary(Staged);

    public static string Summary(TuneSettings s)
    {
        {
            var parts = new List<string>();
            if (s.PowerLimitPct is { } p) parts.Add($"Power {p}%");
            if (s.CoreOffsetMhz is { } c) parts.Add($"Core {c:+0;−0} MHz");
            if (s.MemoryOffsetMhz is { } m) parts.Add($"Memory {m:+0;−0} MHz");
            if (s.VoltageOffsetMv is { } v) parts.Add($"Voltage {v:+0;−0} mV");
            if (s.Fan is not null) parts.Add("Custom fan curve");
            return parts.Count == 0 ? "Factory settings" : string.Join(" · ", parts);
        }
    }

    public AsyncRelayCommand AcceptWaiverCommand { get; }

    /// <summary>Intel requires accepting its overclocking terms before any change; until then the card is monitor-only.</summary>
    public bool NeedsWaiver => _shell.Device is { IntelWaiverAccepted: false };

    public const string WaiverText = "Intel asks you to accept that overclocking may shorten your card's life and isn't covered by its warranty. Redline still keeps every change inside safe limits, with keep-or-revert.";

    /// <summary>A short heads-up for AMD and Intel cards, where Redline's tuning is newer than on NVIDIA.</summary>
    public string? VendorNote => _shell.Device?.Vendor switch
    {
        Vendor.Amd => "AMD tuning is new in Redline and hasn't been tried on every Radeon yet. Every change still goes through keep-or-revert.",
        Vendor.Intel => "Intel Arc tuning is new in Redline and hasn't been tried on every card yet. Every change still goes through keep-or-revert.",
        _ => null,
    };

    internal void OnState()
    {
        OnPropertyChanged(nameof(NeedsWaiver));
        OnPropertyChanged(nameof(VendorNote));
        var d = _shell.Device;
        var caps = d?.Caps ?? Capabilities.MonitorOnly;
        var wanted = new[]
        {
            (Control.PowerLimit, "Power limit", "%"), (Control.CoreOffset, "Core clock", "MHz"),
            (Control.MemoryOffset, "Memory clock", "MHz"), (Control.VoltageOffset, "Voltage", "mV"),
        }.Where(x => caps.Supports(x.Item1)).ToList();
        if (!Sliders.Select(s => s.Control).SequenceEqual(wanted.Select(w => w.Item1)))
        {
            Sliders.Clear();
            foreach (var (c, label, unit) in wanted) Sliders.Add(new SliderViewModel(c, label, unit, Changed));
            OnPropertyChanged(nameof(CanTune));
        }
        RebuildRanges();
        if (!CountdownVisible) LoadFromActive();
    }

    void RebuildRanges()
    {
        if (_shell.Device is not { } d) return;
        var safety = Safety;
        foreach (var s in Sliders)
        {
            var driver = d.Caps.Range(s.Control)!;
            var r = SafetyPolicy.SafeRange(s.Control, driver, safety.Advanced);
            if (s.Control == Control.PowerLimit && safety.AllowPowerAbove100) r = driver;
            s.Step = r.Step; s.Min = r.Min; s.Max = r.Max; s.Default = driver.Default;
            if (s.Value > s.Max || s.Value < s.Min) s.SetSilently(Math.Clamp(s.Value, s.Min, s.Max));
        }
        Changed();
    }

    void LoadFromActive()
    {
        if (_shell.Device is not { } d) return;
        foreach (var s in Sliders) s.SetSilently(d.Active.Get(s.Control) ?? s.Default);
        Changed();
    }

    void Changed()
    {
        OnPropertyChanged(nameof(HasChanges)); OnPropertyChanged(nameof(ApplySummary)); OnPropertyChanged(nameof(NeedsConnectorWarning));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    async Task ApplyAsync()
    {
        if (_shell.Device is not { } d) return;
        var r = await _shell.SendAsync(MessageTypes.StageApply, new StageApplyRequest(d.Id, Staged, Safety));
        if (r?.PayloadAs<StageApplyResponse>() is not { } resp) return;
        _token = resp.Token;
        OnPropertyChanged(nameof(PendingToken));
        SecondsLeft = resp.Seconds;
        CountdownVisible = true;
    }

    internal void OnTick(KeepOrRevertTickEvent k)
    {
        if (k.Token != _token) return;
        SecondsLeft = k.SecondsLeft;
        if (!k.Done) return;
        CountdownVisible = false;
        if (k.Outcome == KeepOrRevertOutcome.Reverted) _shell.LastError = "Change reverted because it wasn't confirmed.";
    }
}
