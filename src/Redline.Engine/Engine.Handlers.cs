using Redline.Core.AutoTune;
using Redline.Core.Games;
using Redline.Core.Gpu;
using Redline.Core.History;
using Redline.Core.Safety;
using Redline.Core.Storage;
using Redline.Core.Stats;
using Redline.Ipc;

namespace Redline.Engine;

public sealed partial class Engine
{
    public async Task<IpcMessage> HandleAsync(IpcMessage req, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return req.Type switch
            {
                MessageTypes.GetState => IpcMessage.OkFor(req.Id, BuildState()),
                MessageTypes.SubscribeTelemetry or MessageTypes.UnsubscribeTelemetry => IpcMessage.OkFor(req.Id),
                MessageTypes.StageApply => await StageApplyAsync(req),
                MessageTypes.ConfirmKeep => Session(req, s => s.Confirm()),
                MessageTypes.RevertNow => Session(req, s => s.RevertNow()),
                MessageTypes.ApplyProfile => await ApplyProfileAsync(req),
                MessageTypes.ResetFactory => await ResetFactoryAsync(req),
                MessageTypes.Undo => await UndoAsync(req),
                MessageTypes.SaveProfile => Profiles(req, () => _profiles.Update(Need<ProfileRequest>(req).Profile)),
                MessageTypes.AddProfile => Profiles(req, () => { var a = Need<AddProfileRequest>(req); _profiles.Add(a.Name, a.Icon); }),
                MessageTypes.DeleteProfile => Profiles(req, () => _profiles.Delete(Need<IdRequest>(req).Id)),
                MessageTypes.RenameProfile => Profiles(req, () => { var r = Need<RenameProfileRequest>(req); _profiles.Rename(r.Id, r.Name); }),
                MessageTypes.StartAutoTune => StartAutoTune(req),
                MessageTypes.CancelAutoTune => CancelAutoTune(req),
                MessageTypes.UpdateSettings => UpdateSettings(req),
                MessageTypes.GetHistory => IpcMessage.OkFor(req.Id, new HistoryResponse(_history.Recent(SlotFor(Need<DeviceRequest>(req).DeviceId).Dev.Id))),
                MessageTypes.AcceptIntelWaiver => AcceptWaiver(req),
                MessageTypes.GetWeekly => IpcMessage.OkFor(req.Id, new WeeklyResponse(_weekly.Summary(_settings.ElectricityRatePerKwh))),
                MessageTypes.Quit => await QuitAsync(req),
                MessageTypes.GetSeries => IpcMessage.OkFor(req.Id, new SeriesResponse(_statsHistory.Query(Need<SeriesRequest>(req).Range))),
                MessageTypes.ImportProfile => ImportProfile(req),
                MessageTypes.ExportReport => IpcMessage.OkFor(req.Id, new ReportResponse(ReportFiles())),
                MessageTypes.RunBeforeAfter => StartBeforeAfter(req),
                _ => await ExtraAsync(req),
            };
        }
        catch (Exception e) when (e is KeyNotFoundException or ArgumentException or InvalidOperationException or IpcProtocolException
                                       or System.Text.Json.JsonException or NotSupportedException)
        {
            return IpcMessage.Error(req.Id, e.Message);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Requests implemented by later features (before/after check, report export).</summary>
    Task<IpcMessage> ExtraAsync(IpcMessage req) =>
        ExtraHandler is { } h ? h(req) : Task.FromResult(IpcMessage.Error(req.Id, $"'{req.Type}' isn't available in this version."));

    internal Func<IpcMessage, Task<IpcMessage>>? ExtraHandler { get; set; }

    static T Need<T>(IpcMessage m) => m.PayloadAs<T>() ?? throw new ArgumentException($"{m.Type} needs a payload.");

    IpcMessage Profiles(IpcMessage req, Action a)
    {
        a();
        return IpcMessage.OkFor(req.Id, BuildState());
    }

    async Task<IpcMessage> StageApplyAsync(IpcMessage req)
    {
        var r = Need<StageApplyRequest>(req);
        var s = SlotFor(r.DeviceId);
        if (s.Pending is not null) throw new InvalidOperationException("Keep or revert the current change first.");
        if (AutoTuneTask is { IsCompleted: false }) throw new InvalidOperationException("Auto-tune is running.");
        if (s.Dev is IRequiresWaiver { WaiverAccepted: false }) throw new InvalidOperationException("Accept Intel's overclocking terms first.");

        var v = SafetyPolicy.Validate(r.Settings, s.Dev.Capabilities, r.Safety);
        var before = s.Active;
        var beforeProfile = s.ActiveProfileId;
        var applied = await ApplyLockedAsync(s, v.Clamped, "manual change", confirm: false);
        s.ActiveProfileId = null;
        KeepOrRevertSession? session = null;
        session = new KeepOrRevertSession(before, applied, _time, onDone: o => Track(SettleAsync(s, session!, beforeProfile, o)));
        s.Pending = session;
        s.PendingTicker = _time.CreateTimer(_ => Emit(MessageTypes.KeepOrRevertTick,
            new KeepOrRevertTickEvent(session.Token, (int)Math.Ceiling(session.Remaining.TotalSeconds), false, null)),
            null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        return IpcMessage.OkFor(req.Id, new StageApplyResponse(session.Token, applied, v.Issues, (int)KeepOrRevertSession.DefaultTimeout.TotalSeconds));
    }

    async Task SettleAsync(Slot s, KeepOrRevertSession session, string? beforeProfile, KeepOrRevertOutcome outcome)
    {
        await _gate.WaitAsync();
        try
        {
            if (s.Pending != session) return;          // superseded (factory reset, profile switch, ...)
            CancelPending(s);
            if (outcome == KeepOrRevertOutcome.Kept)
            {
                _journal.MarkConfirmed(s.Dev.Id, session.Applied);
                s.LastGood = session.Applied;
                _history.Push(new HistoryEntry(_time.GetUtcNow(), s.Dev.Id, session.Previous, session.Applied, "Manual change"));
            }
            else
            {
                await ApplyLockedAsync(s, session.Previous, "reverted (not confirmed)", confirm: true);
                s.ActiveProfileId = beforeProfile;
                Toast("Change reverted", "Your previous settings are back because the change wasn't confirmed.");
            }
        }
        finally { _gate.Release(); }
        Emit(MessageTypes.KeepOrRevertTick, new KeepOrRevertTickEvent(session.Token, 0, true, outcome));
        Emit(MessageTypes.State, BuildStateUnlocked());
    }

    HelperState BuildStateUnlocked() => BuildState();

    IpcMessage Session(IpcMessage req, Action<KeepOrRevertSession> act)
    {
        var token = Need<TokenRequest>(req).Token;
        var s = _slots.FirstOrDefault(x => x.Pending?.Token == token) ?? throw new InvalidOperationException("That change already finished.");
        act(s.Pending!);
        return IpcMessage.OkFor(req.Id);
    }

    async Task<IpcMessage> ApplyProfileAsync(IpcMessage req)
    {
        var r = Need<ApplyProfileRequest>(req);
        var s = SlotFor(r.DeviceId);
        if (AutoTuneTask is { IsCompleted: false }) throw new InvalidOperationException("Auto-tune is running.");
        var p = TryProfile(r.ProfileId) ?? throw new KeyNotFoundException($"No profile '{r.ProfileId}'.");
        if (s.Dev is IRequiresWaiver { WaiverAccepted: false } && !p.Gpu.IsFactory) throw new InvalidOperationException("Accept Intel's overclocking terms first.");
        await ApplyProfileLockedAsync(s, p, "chosen by you");
        if (s == Primary) SetBaseProfile(p.Id);
        return IpcMessage.OkFor(req.Id, BuildState());
    }

    void SetBaseProfile(string id)
    {
        _switcher.SetBase(id);
        if (_settings.BaseProfileId != id) { _settings = _settings with { BaseProfileId = id }; _settingsStore.Save(_settings); }
    }

    async Task<IpcMessage> ResetFactoryAsync(IpcMessage req)
    {
        var s = SlotFor(Need<DeviceRequest>(req).DeviceId);
        await StopAutoTuneStepsAsync();
        CancelPending(s);
        var before = s.Active;
        await ApplyLockedAsync(s, TuneSettings.Factory, "factory", confirm: true);
        s.ActiveProfileId = Profile.FactoryId;
        if (s == Primary) { _cpuBoost.RestoreOriginal(); SetBaseProfile(Profile.FactoryId); }
        if (!before.IsFactory) _history.Push(new HistoryEntry(_time.GetUtcNow(), s.Dev.Id, before, TuneSettings.Factory, "Back to factory"));
        return IpcMessage.OkFor(req.Id, BuildState());
    }

    async Task<IpcMessage> UndoAsync(IpcMessage req)
    {
        var s = SlotFor(Need<DeviceRequest>(req).DeviceId);
        CancelPending(s);
        var e = _history.Undo(s.Dev.Id) ?? throw new InvalidOperationException("Nothing to undo.");
        await ApplyLockedAsync(s, SafetyPolicy.Validate(e.Before, s.Dev.Capabilities, _settings.Safety).Clamped, "undo", confirm: true);
        s.ActiveProfileId = s.Active.IsFactory ? Profile.FactoryId : null;
        return IpcMessage.OkFor(req.Id, BuildState());
    }

    IpcMessage StartAutoTune(IpcMessage req)
    {
        var r = Need<StartAutoTuneRequest>(req);
        var s = SlotFor(r.DeviceId);
        if (AutoTuneTask is { IsCompleted: false }) throw new InvalidOperationException("Auto-tune is already running.");
        if (r.TargetProfileId == Profile.FactoryId) throw new InvalidOperationException("Choose a profile other than Factory.");
        if (s.Dev is IRequiresWaiver { WaiverAccepted: false }) throw new InvalidOperationException("Accept Intel's overclocking terms first.");
        var target = TryProfile(r.TargetProfileId) ?? throw new KeyNotFoundException($"No profile '{r.TargetProfileId}'.");
        CancelPending(s);
        var cts = _autoTuneCts = new CancellationTokenSource();
        var opts = r.Options with
        {
            AllowPowerAbove100 = r.Options.AllowPowerAbove100 && _settings.Safety.AllowPowerAbove100,
            KeepFrom = r.Options.MemoryOnly ? s.Active : null,      // memory-only keeps what's on the card, never the app's numbers
        };
        var tuner = new AutoTuner(s.Dev, _stress, _journal, _settings.Safety);
        var progress = new Progress(p => Emit(MessageTypes.AutoTuneProgress, new AutoTuneProgressEvent(s.Dev.Id, p, null)));
        var run = Task.Run(() => tuner.RunAsync(opts, progress, cts.Token));
        _autoTuneRun = run;
        AutoTuneTask = Task.Run(async () =>
        {
            var result = await run;
            await _gate.WaitAsync();
            try
            {
                s.Active = await s.Dev.ReadAsync(CancellationToken.None);
                _journal.MarkConfirmed(s.Dev.Id, s.Active);
                s.LastGood = s.Active;
                if (result.Outcome == AutoTuneOutcome.Completed && !result.Best.IsFactory)
                {
                    var measured = new Core.Stats.BeforeAfter(result.BaselineScore, result.FinalScore, 0, 0, result.BaselinePowerW, result.FinalPowerW, 0, 0);
                    var updated = target with { Gpu = result.Best with { Fan = target.Gpu.Fan }, StabilityTested = true, TestedAt = _time.GetUtcNow(), Measured = measured };
                    _profiles.Update(updated);
                    await ApplyProfileLockedAsync(s, updated, "auto-tune result");
                    if (s == Primary) SetBaseProfile(updated.Id);
                }
                else if (result.Outcome == AutoTuneOutcome.Cancelled && !s.Active.IsFactory)
                {
                    // Stopped early: keep what passed in the profile, but it skipped the final check, so it isn't
                    // marked as fully tested (and won't be used for "start with Windows").
                    _profiles.Update(target with { Gpu = s.Active with { Fan = target.Gpu.Fan }, StabilityTested = false, TestedAt = null, Measured = null });
                    s.ActiveProfileId = target.Id;
                }
                else s.ActiveProfileId = s.Active.IsFactory ? Profile.FactoryId : null;
            }
            finally { _gate.Release(); }
            Emit(MessageTypes.AutoTuneProgress, new AutoTuneProgressEvent(s.Dev.Id, new AutoTuneProgress(1, "Done", result.Best, TimeSpan.Zero), result));
            Emit(MessageTypes.State, BuildState());
        });
        Track(AutoTuneTask);
        return IpcMessage.OkFor(req.Id);
    }

    sealed class Progress(Action<AutoTuneProgress> a) : IProgress<AutoTuneProgress> { public void Report(AutoTuneProgress v) => a(v); }

    IpcMessage ImportProfile(IpcMessage req)
    {
        var p = Need<ImportProfileRequest>(req).Profile;
        var added = _profiles.Add(p.Name, p.Icon);
        var caps = Primary?.Dev.Capabilities ?? Capabilities.MonitorOnly;
        var gpu = SafetyPolicy.Validate(p.Gpu, caps, SafetyOptions.Default).Clamped;
        _profiles.Update(added with { Gpu = gpu, Cpu = p.Cpu, StabilityTested = false, TestedAt = null, Measured = null });
        return IpcMessage.OkFor(req.Id, BuildState());
    }

    IpcMessage StartBeforeAfter(IpcMessage req)
    {
        var r = Need<BeforeAfterRequest>(req);
        var s = SlotFor(r.DeviceId);
        if (Busy) throw new InvalidOperationException("Another test is running.");
        var p = TryProfile(r.ProfileId) ?? throw new KeyNotFoundException($"No profile '{r.ProfileId}'.");
        if (p.Gpu.IsFactory) throw new InvalidOperationException("That profile is the same as factory, so there's nothing to compare.");
        CancelPending(s);
        var tuned = SafetyPolicy.Validate(p.Gpu, s.Dev.Capabilities, _settings.Safety).Clamped;
        var previous = s.Active;
        var previousProfile = s.ActiveProfileId;
        BeforeAfterTask = Task.Run(async () =>
        {
            BeforeAfter? result = null; string? error = null;
            try
            {
                result = await new BeforeAfterRunner(s.Dev, _stress, _journal).RunAsync(tuned, previous, CancellationToken.None);
            }
            catch (Exception e) { error = e.Message; }
            await _gate.WaitAsync();
            try
            {
                s.Active = await s.Dev.ReadAsync(CancellationToken.None);
                s.ActiveProfileId = previousProfile;
                if (result is not null && TryProfile(p.Id) is { } cur && cur.Id != Profile.FactoryId) _profiles.Update(cur with { Measured = result });
            }
            finally { _gate.Release(); }
            Emit(MessageTypes.BeforeAfterResult, new BeforeAfterResultEvent(p.Id, result, error));
            Emit(MessageTypes.State, BuildState());
        });
        Track(BeforeAfterTask);
        return IpcMessage.OkFor(req.Id);
    }

    /// <summary>
    /// Cancels auto-tune and waits for its current step to stop, so it can't put its last good settings back on the
    /// card after we've gone to factory. (Its wrap-up runs later, once the request lock is free.)
    /// </summary>
    async Task StopAutoTuneStepsAsync()
    {
        _autoTuneCts?.Cancel();
        if (_autoTuneRun is { IsCompleted: false } run)
        {
            try { await run.WaitAsync(TimeSpan.FromSeconds(30)); }
            catch (Exception) { /* outcome and errors are handled by the auto-tune task itself */ }
        }
    }

    IpcMessage CancelAutoTune(IpcMessage req)
    {
        _autoTuneCts?.Cancel();
        return IpcMessage.OkFor(req.Id);
    }

    IpcMessage UpdateSettings(IpcMessage req)
    {
        var n = Need<SettingsRequest>(req).Settings;
        if (n.TempCeilingC is < TemperatureGuard.MinCeilingC or > TemperatureGuard.MaxCeilingC)
            throw new ArgumentException($"Temperature limit must be {TemperatureGuard.MinCeilingC}–{TemperatureGuard.MaxCeilingC} °C.");
        if (n.ElectricityRatePerKwh < 0) throw new ArgumentException("Electricity rate can't be negative.");
        _settings = n with { BaseProfileId = _settings.BaseProfileId, PersonalBestMhz = _settings.PersonalBestMhz };
        _settingsStore.Save(_settings);
        foreach (var s in _slots) s.Guard.CeilingC = n.TempCeilingC;
        _switcher = new ProfileSwitcher(n.GameRules, _settings.BaseProfileId);
        _frames?.SetEnabled(n.OverlayEnabled);
        return IpcMessage.OkFor(req.Id, BuildState());
    }

    IpcMessage AcceptWaiver(IpcMessage req)
    {
        var s = SlotFor(Need<DeviceRequest>(req).DeviceId);
        if (s.Dev is IRequiresWaiver w)
        {
            w.AcceptWaiver();
            if (!_settings.AcceptedWaivers.Contains(s.Dev.Id))
            {
                _settings = _settings with { AcceptedWaivers = [.. _settings.AcceptedWaivers, s.Dev.Id] };
                _settingsStore.Save(_settings);
            }
        }
        return IpcMessage.OkFor(req.Id, BuildState());
    }

    async Task<IpcMessage> QuitAsync(IpcMessage req)
    {
        if (!Need<QuitRequest>(req).KeepTune)
        {
            _autoTuneCts?.Cancel();
            await ResetAllLockedAsync("app quit");
            SetBaseProfile(Profile.FactoryId);
        }
        return IpcMessage.OkFor(req.Id);
    }

    static readonly string[] ReportExtensions = [".json", ".log"];

    /// <summary>System summary plus Redline's own data files (top level of the data folder, .json/.log only).</summary>
    Dictionary<string, string> ReportFiles()
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(_o.DataDir))
        {
            if (!ReportExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) continue;
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (fs.Length > 4 * 1024 * 1024) fs.Seek(-4 * 1024 * 1024, SeekOrigin.End);
                files[Path.GetFileName(path)] = new StreamReader(fs).ReadToEnd();
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        var system = new
        {
            helperVersion = _o.HelperVersion,
            os = Environment.OSVersion.VersionString,
            is64Bit = Environment.Is64BitOperatingSystem,
            processors = Environment.ProcessorCount,
            createdUtc = _time.GetUtcNow(),
            state = BuildState(),
        };
        files["system.json"] = System.Text.Json.JsonSerializer.Serialize(system, new System.Text.Json.JsonSerializerOptions(PipeProtocol.Options) { WriteIndented = true });
        return files;
    }
}
