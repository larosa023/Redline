using System.Collections.ObjectModel;
using Redline.Core.Storage;
using Redline.Ipc;

namespace Redline.App.ViewModels;

public sealed record ToastItem(string Title, string Message, string Severity);

/// <summary>Root view model: connection to the helper, current state, status line and child pages.</summary>
public sealed class ShellViewModel : ObservableObject
{
    readonly IHelperClient _helper;
    readonly Action<Action> _post;
    HelperState? _state;
    bool _helperMissing;
    string? _lastError;
    string _theme = "daylight";

    public ShellViewModel(IHelperClient helper, Action<Action> post)
    {
        _helper = helper;
        _post = post;
        Home = new HomeViewModel(this);
        Tune = new TuneViewModel(this);
        Fans = new FansViewModel(this);
        AutoTune = new AutoTuneViewModel(this);
        RepairCommand = new RelayCommand(() => RepairRequested?.Invoke());
        RetryCommand = new AsyncRelayCommand(LoadAsync);
        _helper.EventReceived += m => _post(() => OnEvent(m));
        _helper.Disconnected += () => _post(() => HelperMissing = true);
    }

    public HomeViewModel Home { get; }
    public TuneViewModel Tune { get; }
    public FansViewModel Fans { get; }
    public AutoTuneViewModel AutoTune { get; }
    public RelayCommand RepairCommand { get; }
    public AsyncRelayCommand RetryCommand { get; }
    public ObservableCollection<ToastItem> Toasts { get; } = new();

    /// <summary>Raised when the user asks to repair the helper (the app runs the elevated installer).</summary>
    public event Action? RepairRequested;

    public HelperState? State { get => _state; private set { if (SetProperty(ref _state, value)) OnStateChanged(); } }
    public bool HelperMissing { get => _helperMissing; private set => SetProperty(ref _helperMissing, value); }
    public string? LastError { get => _lastError; set => SetProperty(ref _lastError, value); }
    public string Theme { get => _theme; set => SetProperty(ref _theme, value); }

    public DeviceState? Device => State?.Devices.FirstOrDefault(d => d.Caps.Ranges.Count > 0) ?? State?.Devices.FirstOrDefault();

    public string Status
    {
        get
        {
            if (Device is not { } d) return Copy.StatusFactory;
            if (d.ActiveProfileId == Profile.FactoryId || (d.ActiveProfileId is null && d.Active.IsFactory)) return Copy.StatusFactory;
            if (d.ActiveProfileId is { } id && State!.Profiles.FirstOrDefault(p => p.Id == id) is { } p) return Copy.StatusProfile(p.Name);
            return Copy.StatusCustom;
        }
    }

    public async Task LoadAsync()
    {
        try
        {
            await _helper.ConnectAsync(TimeSpan.FromSeconds(2));
            HelperMissing = false;
            await RefreshAsync();
            await SendAsync(MessageTypes.SubscribeTelemetry, null);
            await Home.LoadWeeklyAsync();
        }
        catch (HelperUnavailableException) { HelperMissing = true; }
    }

    public async Task RefreshAsync()
    {
        if (await SendAsync(MessageTypes.GetState, null) is { } r && r.PayloadAs<HelperState>() is { } s) State = s;
    }

    /// <summary>Sends a request; errors become <see cref="LastError"/> and return null. Responses carrying state refresh it.</summary>
    public async Task<IpcMessage?> SendAsync(string type, object? payload)
    {
        try
        {
            var r = await _helper.RequestAsync(type, payload);
            if (r.Type == MessageTypes.ErrorResponse)
            {
                LastError = r.PayloadAs<ErrorPayload>()?.Message ?? "Something went wrong.";
                return null;
            }
            LastError = null;
            if (type != MessageTypes.GetState && r.Payload is { } p && p.ValueKind == System.Text.Json.JsonValueKind.Object
                && p.TryGetProperty("helperVersion", out _) && r.PayloadAs<HelperState>() is { } st)
                State = st;
            return r;
        }
        catch (HelperUnavailableException)
        {
            HelperMissing = true;
            return null;
        }
    }

    void OnStateChanged()
    {
        OnPropertyChanged(nameof(Device));
        OnPropertyChanged(nameof(Status));
        if (State is { } s) Theme = s.Settings.Theme;
        Home.OnState();
        Tune.OnState();
        Fans.OnState();
        AutoTune.OnState();
    }

    void OnEvent(IpcMessage m)
    {
        switch (m.Type)
        {
            case MessageTypes.State when m.PayloadAs<HelperState>() is { } s: State = s; break;
            case MessageTypes.Telemetry when m.PayloadAs<TelemetryEvent>() is { } t: Home.OnTelemetry(t); break;
            case MessageTypes.KeepOrRevertTick when m.PayloadAs<KeepOrRevertTickEvent>() is { } k:
                Tune.OnTick(k);
                if (k.Done) _ = RefreshAsync();
                break;
            case MessageTypes.AutoTuneProgress when m.PayloadAs<AutoTuneProgressEvent>() is { } a: AutoTune.OnProgress(a); break;
            case MessageTypes.GuardAction when m.PayloadAs<GuardActionEvent>() is { } g:
                AddToast(new ToastItem("Temperature guard", g.Message, "warning"));
                _ = RefreshAsync();
                break;
            case MessageTypes.Toast when m.PayloadAs<ToastEvent>() is { } t: AddToast(new ToastItem(t.Title, t.Message, t.Severity)); break;
        }
    }

    void AddToast(ToastItem t)
    {
        Toasts.Add(t);
        while (Toasts.Count > 5) Toasts.RemoveAt(0);
    }
}
