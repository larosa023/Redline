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
        Stats = new StatsViewModel(this);
        Games = new GamesViewModel(this);
        Cpu = new CpuViewModel(this);
        Tools = new ToolsViewModel(this);
        Overlay = new OverlayViewModel(this);
        Tray = new TrayViewModel(this);
        RepairCommand = new AsyncRelayCommand(RepairAsync);
        RetryCommand = new AsyncRelayCommand(LoadAsync);
        SetThemeCommand = new ParamCommand<string>(k => _ = SetThemeAsync(k));
        _helper.EventReceived += m => _post(() => OnEvent(m));
        _helper.Disconnected += () => _post(OnHelperLost);
    }

    public HomeViewModel Home { get; }
    public TuneViewModel Tune { get; }
    public FansViewModel Fans { get; }
    public AutoTuneViewModel AutoTune { get; }
    public StatsViewModel Stats { get; }
    public GamesViewModel Games { get; }
    public CpuViewModel Cpu { get; }
    public ToolsViewModel Tools { get; }
    public OverlayViewModel Overlay { get; }
    public TrayViewModel Tray { get; }
    public AsyncRelayCommand RepairCommand { get; }
    public AsyncRelayCommand RetryCommand { get; }
    public ParamCommand<string> SetThemeCommand { get; }

    public async Task SetThemeAsync(string key)
    {
        Theme = key;
        if (State is { } s) await SendAsync(MessageTypes.UpdateSettings, new SettingsRequest(s.Settings with { Theme = key }));
    }

    public async Task UpdateSettingsAsync(Func<AppSettings, AppSettings> change)
    {
        if (State is { } s) await SendAsync(MessageTypes.UpdateSettings, new SettingsRequest(change(s.Settings)));
    }
    public ObservableCollection<ToastItem> Toasts { get; } = new();

    /// <summary>Runs the elevated helper installer (one admin prompt). Returns false if it didn't finish (e.g. the prompt was declined).</summary>
    public Func<Task<bool>>? Installer { get; set; }

    /// <summary>Waiting between connection attempts. Replaceable for tests.</summary>
    public Func<TimeSpan, Task> Delay { get; set; } = Task.Delay;

    /// <summary>How long to wait for a freshly installed helper to start (it loads GPU and sensor drivers first).</summary>
    public static readonly TimeSpan HelperStartWait = TimeSpan.FromSeconds(60);
    static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(1);
    static readonly TimeSpan ReconnectEvery = TimeSpan.FromSeconds(3);

    bool _repairing, _reconnecting;

    /// <summary>The helper is being installed and started; the setup screen shows progress instead of buttons.</summary>
    public bool Repairing { get => _repairing; private set { if (SetProperty(ref _repairing, value)) OnPropertyChanged(nameof(NotRepairing)); } }
    public bool NotRepairing => !_repairing;

    async Task RepairAsync()
    {
        if (Installer is null) return;
        Repairing = true;
        try
        {
            if (!await Installer())
            {
                LastError = Copy.HelperSetupDidNotFinish;
                return;
            }
            // The new helper can take several seconds to load drivers before it answers. Keep trying instead of
            // showing the setup screen again (clicking Set up again would restart it and start the wait over).
            for (var waited = TimeSpan.Zero; ; waited += RetryEvery)
            {
                await LoadAsync();
                if (!HelperMissing) return;
                if (waited >= HelperStartWait) break;
                await Delay(RetryEvery);
            }
            LastError = Copy.HelperDidNotStart;
        }
        finally
        {
            Repairing = false;
            if (HelperMissing) StartReconnecting();
        }
    }

    void OnHelperLost()
    {
        HelperMissing = true;
        StartReconnecting();
    }

    /// <summary>While the helper is unreachable, quietly try again every few seconds (e.g. Windows still starting it).</summary>
    void StartReconnecting()
    {
        if (_reconnecting || Repairing) return;
        _reconnecting = true;
        _ = ReconnectLoopAsync();
    }

    async Task ReconnectLoopAsync()
    {
        try
        {
            while (HelperMissing && !Repairing)
            {
                await Delay(ReconnectEvery);
                if (!HelperMissing || Repairing) break;
                await LoadCoreAsync();
            }
        }
        finally { _reconnecting = false; }
    }

    public HelperState? State { get => _state; private set { if (SetProperty(ref _state, value)) OnStateChanged(); } }
    public bool HelperMissing { get => _helperMissing; private set => SetProperty(ref _helperMissing, value); }

    /// <summary>The app's own version; the helper should match it.</summary>
    public string AppVersion { get; init; } =
        (typeof(ShellViewModel).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    /// <summary>The installed helper is from a different version: it needs the one-prompt setup again.</summary>
    public bool HelperOutdated => State is { } s && !string.Equals(Norm(s.HelperVersion), Norm(AppVersion), StringComparison.OrdinalIgnoreCase);

    static string Norm(string v) => v.Split('+')[0];
    public string? LastError { get => _lastError; set => SetProperty(ref _lastError, value); }
    public string Theme { get => _theme; set => SetProperty(ref _theme, value); }

    public DeviceState? Device => State?.Devices.FirstOrDefault(d => d.Caps.Ranges.Count > 0) ?? State?.Devices.FirstOrDefault();

    public string Status
    {
        get
        {
            if (State?.AutoTuneRunning == true)
                return string.IsNullOrEmpty(AutoTune.CurrentLabel) ? "Auto-tune is testing" : $"Auto-tune is testing · {AutoTune.CurrentLabel}";
            if (Device is not { } d) return Copy.StatusFactory;
            if (d.ActiveProfileId == Profile.FactoryId || (d.ActiveProfileId is null && d.Active.IsFactory)) return Copy.StatusFactory;
            if (d.ActiveProfileId is { } id && State!.Profiles.FirstOrDefault(p => p.Id == id) is { } p) return Copy.StatusProfile(p.Name);
            return $"{Copy.StatusCustom} · {TuneViewModel.Summary(d.Active)}";
        }
    }

    public async Task LoadAsync()
    {
        await LoadCoreAsync();
        if (HelperMissing) StartReconnecting();
    }

    async Task LoadCoreAsync()
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
        OnPropertyChanged(nameof(HelperOutdated));
        if (State is { } s) Theme = s.Settings.Theme;
        Home.OnState();
        Tune.OnState();
        Fans.OnState();
        AutoTune.OnState();
        Games.OnState();
        Cpu.OnState();
        Tools.OnState();
        Overlay.OnState();
        Tray.OnStateChanged();
    }

    void OnEvent(IpcMessage m)
    {
        switch (m.Type)
        {
            case MessageTypes.State when m.PayloadAs<HelperState>() is { } s: State = s; break;
            case MessageTypes.Telemetry when m.PayloadAs<TelemetryEvent>() is { } t: Home.OnTelemetry(t); Overlay.OnTelemetry(t); break;
            case MessageTypes.FpsSample when m.PayloadAs<FpsSampleEvent>() is { } f: Overlay.OnFps(f); break;
            case MessageTypes.BeforeAfterResult when m.PayloadAs<BeforeAfterResultEvent>() is { } b: Tools.OnBeforeAfter(b); break;
            case MessageTypes.KeepOrRevertTick when m.PayloadAs<KeepOrRevertTickEvent>() is { } k:
                Tune.OnTick(k);
                if (k.Done) _ = RefreshAsync();
                break;
            case MessageTypes.AutoTuneProgress when m.PayloadAs<AutoTuneProgressEvent>() is { } a: AutoTune.OnProgress(a); OnPropertyChanged(nameof(Status)); break;
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
