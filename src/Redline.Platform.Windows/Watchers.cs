using System.Diagnostics.Eventing.Reader;
using System.Management;
using System.Runtime.Versioning;
using System.ServiceProcess;
using Redline.Engine;

namespace Redline.Platform.Windows;

public static class StabilityEventFilter
{
    /// <summary>System-log events that mean the graphics driver stopped responding and was reset.</summary>
    public static bool IsDriverReset(string provider, int eventId) => provider.ToLowerInvariant() switch
    {
        "display" => eventId == 4101,
        "nvlddmkm" => eventId is 13 or 14 or 153,
        "amdkmdag" or "amdwddmg" => eventId is 4101 or 4097,
        "igfx" or "igfxn" or "igfxnd" => eventId is 4101 or 4097,
        _ => false,
    };
}

/// <summary>Driver resets from the Windows System event log.</summary>
[SupportedOSPlatform("windows")]
public sealed class EventLogStabilitySignals : IStabilitySignals, IDisposable
{
    readonly EventLogWatcher _w;

    public EventLogStabilitySignals()
    {
        var q = new EventLogQuery("System", PathType.LogName, "*[System[(EventID=4101 or EventID=4097 or EventID=13 or EventID=14 or EventID=153)]]");
        _w = new EventLogWatcher(q);
        _w.EventRecordWritten += (_, e) =>
        {
            if (e.EventRecord is { } r && StabilityEventFilter.IsDriverReset(r.ProviderName ?? "", r.Id)) DriverReset?.Invoke("*");
        };
        _w.Enabled = true;
    }

    public event Action<string>? DriverReset;
    public void Dispose() => _w.Dispose();
}

/// <summary>Process start/stop via WMI traces (the helper runs as SYSTEM, which these require).</summary>
[SupportedOSPlatform("windows")]
public sealed class WmiProcessWatcher : IProcessWatcher, IDisposable
{
    readonly ManagementEventWatcher _start = new(new WqlEventQuery("SELECT ProcessName FROM Win32_ProcessStartTrace"));
    readonly ManagementEventWatcher _stop = new(new WqlEventQuery("SELECT ProcessName FROM Win32_ProcessStopTrace"));

    public WmiProcessWatcher()
    {
        _start.EventArrived += (_, e) => { if (e.NewEvent["ProcessName"] is string n) Started?.Invoke(n); };
        _stop.EventArrived += (_, e) => { if (e.NewEvent["ProcessName"] is string n) Stopped?.Invoke(n); };
        _start.Start();
        _stop.Start();
    }

    public event Action<string>? Started;
    public event Action<string>? Stopped;

    public void Dispose()
    {
        _start.Stop(); _stop.Stop();
        _start.Dispose(); _stop.Dispose();
    }
}

[SupportedOSPlatform("windows")]
public static class PawnIoDetector
{
    public static bool IsInstalled()
    {
        try { return ServiceController.GetDevices().Concat(ServiceController.GetServices()).Any(s => s.ServiceName.Equals("PawnIO", StringComparison.OrdinalIgnoreCase)); }
        catch (Exception) { return false; }
    }
}
