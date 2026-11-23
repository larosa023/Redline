using Redline.Core.Gpu;

namespace Redline.Core.History;

public sealed record HistoryEntry(DateTimeOffset At, string DeviceId, TuneSettings Before, TuneSettings After, string Reason);

/// <summary>Per-device log of applied changes, newest first, capped.</summary>
public sealed class UndoHistory(int capacity = 50)
{
    readonly Dictionary<string, LinkedList<HistoryEntry>> _byDevice = new();
    readonly object _gate = new();

    public void Push(HistoryEntry e)
    {
        lock (_gate)
        {
            if (!_byDevice.TryGetValue(e.DeviceId, out var list)) _byDevice[e.DeviceId] = list = new();
            list.AddFirst(e);
            while (list.Count > capacity) list.RemoveLast();
        }
    }

    public bool CanUndo(string deviceId) { lock (_gate) return _byDevice.TryGetValue(deviceId, out var l) && l.Count > 0; }

    public HistoryEntry? Undo(string deviceId)
    {
        lock (_gate)
        {
            if (!_byDevice.TryGetValue(deviceId, out var l) || l.First is null) return null;
            var e = l.First.Value;
            l.RemoveFirst();
            return e;
        }
    }

    public IReadOnlyList<HistoryEntry> Recent(string deviceId)
    {
        lock (_gate) return _byDevice.TryGetValue(deviceId, out var l) ? l.ToList() : [];
    }
}
