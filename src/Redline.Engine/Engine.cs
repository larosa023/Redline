using Redline.Core.AutoTune;
using Redline.Core.Cpu;
using Redline.Core.Drivers;
using Redline.Core.Games;
using Redline.Core.Gpu;
using Redline.Core.History;
using Redline.Core.Safety;
using Redline.Core.Stats;
using Redline.Core.Storage;
using Redline.Ipc;

namespace Redline.Engine;

public sealed record EngineOptions(string DataDir, TimeSpan? PollActive = null, TimeSpan? PollIdle = null,
    TimeSpan? StartupApplyDelay = null, string HelperVersion = "1.0.0")
{
    public TimeSpan Active => PollActive ?? TimeSpan.FromSeconds(1);
    public TimeSpan Idle => PollIdle ?? TimeSpan.FromSeconds(5);
    public TimeSpan StartupDelay => StartupApplyDelay ?? TimeSpan.FromSeconds(15);
}

public interface IProcessWatcher
{
    event Action<string>? Started;
    event Action<string>? Stopped;
}

/// <summary>Frame times per presenting process (PresentMon). Raised about once a second.</summary>
public interface IFrameSource
{
    event Action<string, IReadOnlyList<double>>? Frames;
    /// <summary>FPS tracking runs only while the overlay is on.</summary>
    void SetEnabled(bool enabled);
}

public interface IStabilitySignals
{
    /// <summary>Raised with a device id, or "*" when the driver reset affected all GPUs.</summary>
    event Action<string>? DriverReset;
}

/// <summary>
/// The helper's brain: owns every device, enforces every safety rule, and answers requests from the app.
/// All state changes are serialized through one gate.
/// </summary>
public sealed partial class Engine : IAsyncDisposable, IRequestHandler
{
    sealed class Slot(IGpuDevice dev)
    {
        public IGpuDevice Dev { get; } = dev;
        public TuneSettings Active = TuneSettings.Factory;
        public TuneSettings LastGood = TuneSettings.Factory;
        public string? ActiveProfileId = Profile.FactoryId;
        public KeepOrRevertSession? Pending;
        public ITimer? PendingTicker;
        public TemperatureGuard Guard = new();
        public bool Lost;
        public Telemetry? LastTelemetry;
    }

    readonly EngineOptions _o;
    readonly IEnumerable<IGpuBackend> _backends;
    readonly IStressRunner _stress;
    readonly ICpuMonitor _cpu;
    readonly IProcessWatcher _procs;
    readonly IStabilitySignals _signals;
    readonly TimeProvider _time;
    readonly Func<bool> _pawnIo;
    readonly SemaphoreSlim _gate = new(1, 1);
    readonly List<Slot> _slots = new();
    readonly UndoHistory _history = new();
    readonly SafeStartJournal _journal;
    readonly JsonStore<AppSettings> _settingsStore;
    readonly ProfileStore _profiles;
    readonly CpuBoostController _cpuBoost;
    readonly WeeklyStats _weekly;
    readonly PersonalBest _best = new();
    readonly List<Task> _background = new();
    AppSettings _settings;
    ProfileSwitcher _switcher;
    StartupDecision _lastStartup;
    ITimer? _poll, _startupTimer;
    int _subscribers;
    bool _disposed;
    CancellationTokenSource? _autoTuneCts;
    Task? _autoTuneRun;
    int _runningGames;

    readonly IGpuExtraSensors? _extras;
    readonly IFrameSource? _frames;
    readonly StatsHistory _statsHistory;
    (DateTimeOffset At, double Fps)? _lastFps;

    public Engine(EngineOptions o, IEnumerable<IGpuBackend> backends, IStressRunner stress, IPowerPlan plan, ICpuMonitor cpu,
        IProcessWatcher procs, IStabilitySignals signals, TimeProvider time, Func<bool>? pawnIoInstalled = null,
        IGpuExtraSensors? extras = null, IFrameSource? frames = null)
    {
        _o = o; _backends = backends; _stress = stress; _cpu = cpu; _procs = procs; _signals = signals; _time = time;
        _extras = extras; _frames = frames;
        _statsHistory = new StatsHistory(time);
        _pawnIo = pawnIoInstalled ?? (() => false);
        Directory.CreateDirectory(o.DataDir);
        _journal = new SafeStartJournal(Path.Combine(o.DataDir, "journal.json"));
        _settingsStore = new JsonStore<AppSettings>(Path.Combine(o.DataDir, "settings.json"), () => new AppSettings());
        _profiles = new ProfileStore(o.DataDir);
        _cpuBoost = new CpuBoostController(plan, Path.Combine(o.DataDir, "cpu-original.json"));
        _weekly = new WeeklyStats(time);
        _settings = _settingsStore.Load();
        _switcher = new ProfileSwitcher(_settings.GameRules, _settings.BaseProfileId);
        _best.Restore(_settings.PersonalBestMhz);
    }

    /// <summary>Raised for every event the app should see (telemetry, ticks, toasts, ...).</summary>
    public event Action<IpcMessage>? Event;

    public Task? AutoTuneTask { get; private set; }
    public Task? BeforeAfterTask { get; private set; }

    bool Busy => AutoTuneTask is { IsCompleted: false } || BeforeAfterTask is { IsCompleted: false };

    void OnFrames(string process, IReadOnlyList<double> frameTimesMs)
    {
        if (frameTimesMs.Count == 0) return;
        var (avg, low) = FrameStats.From(frameTimesMs);
        _lastFps = (_time.GetUtcNow(), avg);
        Emit(MessageTypes.FpsSample, new FpsSampleEvent(process, avg, low));
    }

    public TimeSpan CurrentPollInterval => _subscribers > 0 ? _o.Active : _o.Idle;

    public int SubscriberCount
    {
        get => Volatile.Read(ref _subscribers);
        set
        {
            var before = CurrentPollInterval;
            Volatile.Write(ref _subscribers, Math.Max(0, value));
            if (CurrentPollInterval != before) _poll?.Change(CurrentPollInterval, CurrentPollInterval);
        }
    }

    Slot? Primary => _slots.FirstOrDefault(s => s.Dev.Capabilities.Ranges.Count > 0) ?? _slots.FirstOrDefault();

    // ---------------- lifecycle ----------------

    public async Task StartAsync(CancellationToken ct)
    {
        DiscoveryResult found;
        await _gate.WaitAsync(ct);
        try
        {
            found = GpuDiscovery.DiscoverAll(_backends);
            _slots.AddRange(found.Devices.Select(d => new Slot(d) { Guard = new TemperatureGuard(_settings.TempCeilingC) }));
            foreach (var w in _slots.Select(x => x.Dev).OfType<IRequiresWaiver>().Where(w => !w.WaiverAccepted && _settings.AcceptedWaivers.Contains(((IGpuDevice)w).Id)))
            {
                try { w.AcceptWaiver(); } catch (Exception) { /* stays monitor-only until accepted again */ }
            }

            Core.Diagnostics.HelperLog.Write($"found {found.Devices.Count} GPU(s): {string.Join(", ", found.Devices.Select(d => d.Name))}");
            var (decision, _) = _journal.Inspect();
            _lastStartup = decision;
            if (decision == StartupDecision.RecoverToFactory)
            {
                foreach (var s in _slots) await TryResetAsync(s);
                _cpuBoost.RestoreOriginal();
            }
            _journal.MarkStarted();
            Core.Diagnostics.HelperLog.Write($"startup: {decision}");

            foreach (var s in _slots)
            {
                try { s.Active = await s.Dev.ReadAsync(ct); } catch (Exception) { s.Active = TuneSettings.Factory; }
                s.ActiveProfileId = s.Active.IsFactory ? Profile.FactoryId : null;
                _journal.MarkConfirmed(s.Dev.Id, s.Active);
            }

            TrimProfilesToSafety();

            if (_settings.StartWithWindows && _settings.StartupProfileId is { } pid && TryProfile(pid) is { StabilityTested: true })
                _startupTimer = _time.CreateTimer(_ => Track(StartupApplyAsync(pid)), null, _o.StartupDelay, Timeout.InfiniteTimeSpan);

            _procs.Started += OnGameStarted;
            _procs.Stopped += OnGameStopped;
            _signals.DriverReset += OnDriverReset;
            if (_frames is not null) { _frames.Frames += OnFrames; _frames.SetEnabled(_settings.OverlayEnabled); }
            _poll = _time.CreateTimer(_ => Track(TickAsync()), null, CurrentPollInterval, CurrentPollInterval);
        }
        finally { _gate.Release(); }
        foreach (var (vendor, error) in found.Errors)
            Toast($"Couldn't read your {vendor} graphics card", $"Monitoring and tuning for it are unavailable: {error}", "warning");
        if (_lastStartup == StartupDecision.RecoverToFactory)
            Toast("Back to factory settings", "Your PC didn't shut down cleanly while a tune was active, so Redline restored factory settings. Reapply your profile when you're ready.", "warning");
    }

    /// <summary>
    /// Profiles hold what Redline will actually apply. One saved with more than the safety settings allow (e.g. 111%
    /// power with "allow above 100%" off) is trimmed, and its measurement dropped because it described the old values.
    /// </summary>
    void TrimProfilesToSafety()
    {
        if (Primary is not { } p) return;
        foreach (var prof in _profiles.All().Where(x => !x.Gpu.IsFactory))
        {
            var clamped = SafetyPolicy.Validate(prof.Gpu, p.Dev.Capabilities, _settings.Safety).Clamped;
            if (clamped.PowerLimitPct == 100) clamped = clamped with { PowerLimitPct = null };
            clamped = clamped with { Fan = prof.Gpu.Fan };
            if (clamped == prof.Gpu) continue;
            _profiles.Update(prof with { Gpu = clamped, Measured = null });
            Core.Diagnostics.HelperLog.Write($"profile {prof.Id} trimmed to safety limits");
        }
    }

    async Task StartupApplyAsync(string profileId)
    {
        await _gate.WaitAsync();
        try
        {
            if (Primary is { } p && TryProfile(profileId) is { StabilityTested: true } prof) await ApplyProfileLockedAsync(p, prof, "start with Windows");
        }
        finally { _gate.Release(); }
    }

    /// <summary>Service stop. keepTune=false returns everything to factory first.</summary>
    public async Task StopAsync(bool keepTune)
    {
        // Let a running auto-tune step back to its last passing settings before the process exits,
        // instead of leaving the untested step on the card.
        await StopAutoTuneStepsAsync();
        await _gate.WaitAsync();
        try
        {
            _poll?.Dispose(); _startupTimer?.Dispose();
            if (!keepTune) await ResetAllLockedAsync("Redline helper stopped");
            _journal.MarkCleanShutdown();
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _poll?.Dispose(); _startupTimer?.Dispose();
        _autoTuneCts?.Cancel();
        _procs.Started -= OnGameStarted; _procs.Stopped -= OnGameStopped; _signals.DriverReset -= OnDriverReset;
        if (_frames is not null) _frames.Frames -= OnFrames;
        foreach (var s in _slots) { s.PendingTicker?.Dispose(); s.Pending?.Dispose(); }
        try { if (AutoTuneTask is { } t) await t; } catch (OperationCanceledException) { }
        try { if (BeforeAfterTask is { } b) await b; } catch (Exception) { }
        await IdleAsync();
    }

    /// <summary>Waits for background work started by timers and events (used by tests and shutdown).</summary>
    internal async Task IdleAsync()
    {
        while (true)
        {
            Task[] pending;
            lock (_background) { _background.RemoveAll(t => t.IsCompleted); pending = _background.ToArray(); }
            if (pending.Length == 0) return;
            try { await Task.WhenAll(pending); } catch (Exception) { /* surfaced via toasts */ }
        }
    }

    void Track(Task t) { lock (_background) _background.Add(t); }

    void Emit<T>(string type, T payload) => Event?.Invoke(IpcMessage.Create(0, type, payload));

    void Toast(string title, string msg, string severity = "info") => Emit(MessageTypes.Toast, new ToastEvent(title, msg, severity));

    // ---------------- helpers ----------------

    Profile? TryProfile(string id) => _profiles.All().FirstOrDefault(p => p.Id == id);

    Slot SlotFor(string id) => _slots.FirstOrDefault(s => s.Dev.Id == id) ?? throw new KeyNotFoundException($"No GPU with id '{id}'.");

    async Task<TuneSettings> ApplyLockedAsync(Slot s, TuneSettings settings, string reason, bool confirm)
    {
        _journal.MarkPending(s.Dev.Id, settings, reason);
        var r = settings.IsFactory ? await ResetDeviceAsync(s) : await s.Dev.ApplyAsync(settings, CancellationToken.None);
        s.Active = r.Actual;
        if (confirm) { _journal.MarkConfirmed(s.Dev.Id, r.Actual); s.LastGood = r.Actual; }
        if (s.Active.Fan is null && s.Dev is IFanPercentControl f) await f.SetFanAutoAsync(CancellationToken.None);
        return r.Actual;
    }

    static async Task<ApplyResult> ResetDeviceAsync(Slot s)
    {
        await s.Dev.ResetAsync(CancellationToken.None);
        return new ApplyResult(TuneSettings.Factory, TuneSettings.Factory);
    }

    async Task TryResetAsync(Slot s)
    {
        try { await s.Dev.ResetAsync(CancellationToken.None); } catch (Exception) { /* device may be gone; nothing else to do */ }
        s.Active = TuneSettings.Factory;
        s.ActiveProfileId = Profile.FactoryId;
        _journal.MarkConfirmed(s.Dev.Id, TuneSettings.Factory);
    }

    async Task ResetAllLockedAsync(string reason)
    {
        foreach (var s in _slots)
        {
            CancelPending(s);
            await TryResetAsync(s);
        }
        _cpuBoost.RestoreOriginal();
        _ = reason;
    }

    void CancelPending(Slot s)
    {
        s.PendingTicker?.Dispose(); s.PendingTicker = null;
        s.Pending?.Dispose(); s.Pending = null;
    }

    async Task ApplyProfileLockedAsync(Slot s, Profile p, string reason)
    {
        CancelPending(s);
        var v = SafetyPolicy.Validate(p.Gpu, s.Dev.Capabilities, _settings.Safety);
        var before = s.Active;
        await ApplyLockedAsync(s, v.Clamped, $"profile {p.Name} ({reason})", confirm: true);
        s.ActiveProfileId = p.Id;
        if (s == Primary)
        {
            if (p.Id == Profile.FactoryId) _cpuBoost.RestoreOriginal(); else _cpuBoost.Apply(p.Cpu);
        }
        if (before != s.Active) _history.Push(new HistoryEntry(_time.GetUtcNow(), s.Dev.Id, before, s.Active, $"Profile: {p.Name}"));
    }

    HelperState BuildState()
    {
        var devices = _slots.Select(s => new DeviceState(s.Dev.Id, s.Dev.Name, s.Dev.Vendor, s.Dev.Capabilities, s.Active, s.ActiveProfileId,
            DriverPolicy.Check(s.Dev.Vendor, s.Dev.DriverVersion), s.Dev is not IRequiresWaiver w || w.WaiverAccepted, !s.Lost)).ToList();
        return new HelperState(_o.HelperVersion, devices, SafeCpu(), _profiles.All(), _settings, _pawnIo(), _lastStartup,
            _autoTuneRun is { IsCompleted: false }, _best.Best, _best.IsNewBest);
    }

    CpuState? SafeCpu()
    {
        try { return _cpu.Read(); } catch (Exception) { return null; }
    }
}
