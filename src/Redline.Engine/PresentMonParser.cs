using System.Globalization;

namespace Redline.Engine;

/// <summary>Parses PresentMon CSV lines and hands back the frame times of the busiest (game) process since the last call.</summary>
public sealed class PresentMonParser
{
    static readonly HashSet<string> System = new(StringComparer.OrdinalIgnoreCase)
        { "dwm.exe", "explorer.exe", "Redline.exe", "SearchHost.exe", "StartMenuExperienceHost.exe", "TextInputHost.exe", "ShellExperienceHost.exe" };

    int _app = -1, _ft = -1;
    readonly Dictionary<string, List<double>> _frames = new(StringComparer.OrdinalIgnoreCase);
    readonly object _gate = new();

    public void Feed(string line)
    {
        var cols = line.Split(',');
        if (_app < 0)
        {
            _app = Array.FindIndex(cols, c => c.Trim() == "Application");
            _ft = Array.FindIndex(cols, c => c.Trim() is "MsBetweenPresents" or "FrameTime");
            if (_app < 0 || _ft < 0) { _app = -1; throw new FormatException("PresentMon output has no Application/frame-time columns."); }
            return;
        }
        if (cols.Length <= Math.Max(_app, _ft)) return;
        var proc = cols[_app].Trim();
        if (proc.Length == 0 || System.Contains(proc)) return;
        if (!double.TryParse(cols[_ft], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) || ms <= 0 || ms > 1000) return;
        lock (_gate)
        {
            if (!_frames.TryGetValue(proc, out var l)) _frames[proc] = l = new List<double>();
            l.Add(ms);
        }
    }

    public (string Process, IReadOnlyList<double> FrameTimesMs)? TakeBusiest()
    {
        lock (_gate)
        {
            var best = _frames.OrderByDescending(kv => kv.Value.Count).FirstOrDefault();
            _frames.Clear();
            return best.Value is { Count: > 0 } ? (best.Key, best.Value) : null;
        }
    }
}
