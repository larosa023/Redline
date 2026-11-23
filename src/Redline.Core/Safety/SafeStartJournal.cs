using System.Text.Json;
using Redline.Core.Gpu;
using Redline.Core.Storage;

namespace Redline.Core.Safety;

public enum StartupDecision { Clean, RecoverToFactory }

public sealed record JournalState(string DeviceId, TuneSettings? Pending, TuneSettings? Active, string? Reason);

/// <summary>
/// Crash-safe record of what is applied. Every change is written (flushed) before it reaches the hardware,
/// so after a crash or power loss the next start knows to return to factory settings.
/// </summary>
public sealed class SafeStartJournal(string path)
{
    sealed record Doc(bool CleanShutdown, Dictionary<string, JournalState> Devices);
    readonly object _gate = new();

    Doc Read()
    {
        if (!File.Exists(path)) return new Doc(true, new());
        return JsonSerializer.Deserialize<Doc>(File.ReadAllText(path), Json.Options) ?? throw new JsonException("empty journal");
    }

    void Update(Func<Doc, Doc> f)
    {
        lock (_gate)
        {
            Doc d;
            try { d = Read(); } catch (JsonException) { d = new Doc(false, new()); }
            Json.WriteAtomic(path, JsonSerializer.Serialize(f(d), Json.Options));
        }
    }

    public void MarkStarted() => Update(d => d with { CleanShutdown = false });
    public void MarkCleanShutdown() => Update(d => d with { CleanShutdown = true });

    public void MarkPending(string deviceId, TuneSettings s, string reason) => Update(d =>
    {
        var cur = d.Devices.GetValueOrDefault(deviceId);
        d.Devices[deviceId] = new JournalState(deviceId, s, cur?.Active, reason);
        return d;
    });

    public void MarkConfirmed(string deviceId, TuneSettings s) => Update(d =>
    {
        d.Devices[deviceId] = new JournalState(deviceId, null, s, null);
        return d;
    });

    public (StartupDecision Decision, IReadOnlyList<JournalState> Devices) Inspect()
    {
        Doc d;
        lock (_gate)
        {
            try { d = Read(); }
            catch (JsonException) { return (StartupDecision.RecoverToFactory, []); }
        }
        var bad = d.Devices.Values
            .Where(s => s.Pending is not null || (!d.CleanShutdown && s.Active is { IsFactory: false }))
            .ToList();
        return (bad.Count > 0 ? StartupDecision.RecoverToFactory : StartupDecision.Clean, bad);
    }
}
