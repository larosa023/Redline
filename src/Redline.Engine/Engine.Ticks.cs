using Redline.Core.Gpu;
using Redline.Core.Safety;
using Redline.Core.Stats;
using Redline.Core.Storage;
using Redline.Ipc;

namespace Redline.Engine;

public sealed partial class Engine
{
    /// <summary>One monitoring pass: telemetry, guard, lost-device recovery, fan curve, stats.</summary>
    internal async Task TickAsync()
    {
        if (_disposed) return;
        var events = new List<(string, object)>();
        await _gate.WaitAsync();
        try
        {
            var cpu = SafeCpu();
            var game = _runningGames > 0;
            foreach (var s in _slots)
            {
                Telemetry? t;
                try { t = s.Dev.ReadTelemetry(); } catch (Exception) { t = null; }

                if (t is null) s.Lost = true;
                else if (s.Lost)
                {
                    s.Lost = false;
                    if (!s.LastGood.IsFactory && s.Pending is null && AutoTuneTask is not { IsCompleted: false })
                    {
                        await ApplyLockedAsync(s, s.LastGood, "reapplied after GPU came back", confirm: true);
                        s.ActiveProfileId = TryActiveProfileFor(s.LastGood);
                        events.Add((MessageTypes.Toast, new ToastEvent("Tune restored", $"{s.Dev.Name} is back, and your tune was reapplied.", "info")));
                    }
                }
                s.LastTelemetry = t;

                if (AutoTuneTask is not { IsCompleted: false })
                {
                    switch (s.Guard.Evaluate(t, s.Active))
                    {
                        case GuardAction.ScaleBack sb:
                            CancelPending(s);
                            await ApplyLockedAsync(s, sb.Reduced, "temperature guard", confirm: true);
                            s.ActiveProfileId = null;
                            events.Add((MessageTypes.GuardAction, new GuardActionEvent(s.Dev.Id, "ScaleBack",
                                $"{s.Dev.Name} stayed at {_settings.TempCeilingC} °C or hotter, so Redline eased off your tune.")));
                            break;
                        case GuardAction.RevertToFactory rv:
                            CancelPending(s);
                            var keep = s.LastGood;
                            await TryApplyFactoryAsync(s, "temperature guard");
                            if (t is null) s.LastGood = keep;             // device lost: restore when it returns
                            events.Add((MessageTypes.GuardAction, new GuardActionEvent(s.Dev.Id, "RevertToFactory", rv.Reason + " Redline returned to factory settings.")));
                            break;
                    }
                }

                if (t is not null && s.Active.Fan is { } curve && s.Dev is IFanPercentControl fan)
                {
                    try { await fan.SetFanPercentAsync(curve.Evaluate(t.TempC), CancellationToken.None); } catch (Exception) { }
                }

                if (t is not null && s == Primary)
                {
                    var prof = s.ActiveProfileId is { } pid ? TryProfile(pid) : null;
                    _weekly.Add(new StatSample(_time.GetUtcNow(), t.PowerW, game, s.ActiveProfileId ?? "custom", false), prof?.Measured?.StockPowerW);
                    var tested = prof is { StabilityTested: true } && prof.Id != Profile.FactoryId;
                    _best.Observe(_time.GetUtcNow(), t.CoreClockMhz, tested);
                    if (_best.IsNewBest && _best.Best != _settings.PersonalBestMhz)
                    {
                        _settings = _settings with { PersonalBestMhz = _best.Best };
                        _settingsStore.Save(_settings);
                    }
                }

                events.Add((MessageTypes.Telemetry, new TelemetryEvent(s.Dev.Id, t, cpu, game)));
            }
        }
        finally { _gate.Release(); }
        foreach (var (type, payload) in events) Event?.Invoke(IpcMessage.Create(0, type, payload));
    }

    string? TryActiveProfileFor(TuneSettings s) =>
        _profiles.All().FirstOrDefault(p => p.Gpu == s)?.Id ?? (s.IsFactory ? Profile.FactoryId : null);

    async Task TryApplyFactoryAsync(Slot s, string reason)
    {
        try { await ApplyLockedAsync(s, TuneSettings.Factory, reason, confirm: true); }
        catch (Exception) { s.Active = TuneSettings.Factory; _journal.MarkConfirmed(s.Dev.Id, TuneSettings.Factory); }
        s.ActiveProfileId = Profile.FactoryId;
    }

    void OnDriverReset(string deviceId) => Track(DriverResetAsync(deviceId));

    async Task DriverResetAsync(string deviceId)
    {
        var any = false;
        await _gate.WaitAsync();
        try
        {
            foreach (var s in _slots.Where(x => deviceId == "*" || x.Dev.Id == deviceId))
            {
                if (s.Active.IsFactory) continue;
                CancelPending(s);
                var keep = s.LastGood;
                await TryApplyFactoryAsync(s, "graphics driver reset");
                s.LastGood = TuneSettings.Factory;
                _ = keep;
                any = true;
                var t = s.LastTelemetry;
                _weekly.Add(new StatSample(_time.GetUtcNow(), t?.PowerW ?? 0, _runningGames > 0, "factory", true), null);
            }
        }
        finally { _gate.Release(); }
        if (any)
        {
            Toast("Graphics driver restarted", "Your graphics driver reset while a tune was active, so Redline switched to factory settings. If this keeps happening, try a gentler profile.", "warning");
            Emit(MessageTypes.State, BuildState());
        }
    }

    void OnGameStarted(string exe) => Track(GameAsync(exe, started: true));
    void OnGameStopped(string exe) => Track(GameAsync(exe, started: false));

    async Task GameAsync(string exe, bool started)
    {
        string? applied = null;
        await _gate.WaitAsync();
        try
        {
            var isGame = _settings.GameRules.Any(r => string.Equals(Path.GetFileName(r.ExeName), Path.GetFileName(exe), StringComparison.OrdinalIgnoreCase));
            if (isGame) _runningGames = Math.Max(0, _runningGames + (started ? 1 : -1));
            var id = started ? _switcher.OnProcessStarted(exe) : _switcher.OnProcessStopped(exe);
            if (id is null || Primary is not { } p || AutoTuneTask is { IsCompleted: false }) return;
            if (TryProfile(id) is not { } prof) return;
            if (p.Dev is IRequiresWaiver { WaiverAccepted: false } && !prof.Gpu.IsFactory) return;
            await ApplyProfileLockedAsync(p, prof, started ? $"{exe} started" : $"{exe} closed");
            applied = prof.Name;
        }
        finally { _gate.Release(); }
        if (applied is not null)
        {
            Toast($"{applied} is on", started ? $"Switched for {Path.GetFileNameWithoutExtension(exe)}." : "Game closed, switched back.");
            Emit(MessageTypes.State, BuildState());
        }
    }
}
