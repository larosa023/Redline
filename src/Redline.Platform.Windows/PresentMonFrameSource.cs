using System.Diagnostics;
using System.Runtime.Versioning;
using Redline.Engine;

namespace Redline.Platform.Windows;

/// <summary>
/// Runs PresentMon (bundled next to the helper) while the overlay is on and reports the busiest game's frame times once a second.
/// Uses Windows event tracing only — nothing is injected into games.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PresentMonFrameSource(string presentMonExe) : IFrameSource, IDisposable
{
    readonly object _gate = new();
    Process? _proc;
    Timer? _timer;
    PresentMonParser? _parser;

    public event Action<string, IReadOnlyList<double>>? Frames;

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (enabled && _proc is null && File.Exists(presentMonExe)) Start();
            else if (!enabled) Stop();
        }
    }

    void Start()
    {
        var parser = _parser = new PresentMonParser();
        var p = new Process
        {
            StartInfo = new ProcessStartInfo(presentMonExe,
                "--output_stdout --no_console_stats --stop_existing_session --session_name RedlineOverlay --v1_metrics")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true },
            EnableRaisingEvents = true,
        };
        p.OutputDataReceived += (_, e) => { if (e.Data is { } line) { try { parser.Feed(line); } catch (FormatException) { } } };
        p.Exited += (_, _) => { lock (_gate) { if (_proc == p) _proc = null; } };
        p.Start();
        p.BeginOutputReadLine();
        _proc = p;
        _timer = new Timer(_ => { if (_parser?.TakeBusiest() is { } b) Frames?.Invoke(b.Process, b.FrameTimesMs); }, null, 1000, 1000);
    }

    void Stop()
    {
        _timer?.Dispose(); _timer = null;
        try { _proc?.Kill(entireProcessTree: true); } catch (Exception) { }
        _proc?.Dispose(); _proc = null;
    }

    public void Dispose() { lock (_gate) Stop(); }
}
