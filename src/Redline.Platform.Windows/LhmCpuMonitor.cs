using LibreHardwareMonitor.Hardware;
using Redline.Core.Cpu;
using Redline.Core.Gpu;

namespace Redline.Platform.Windows;

/// <summary>CPU, RAM and extra GPU sensors via LibreHardwareMonitor. Without PawnIO, CPU temperature/power may be missing.</summary>
public sealed class LhmCpuMonitor : ICpuMonitor, IGpuExtraSensors, IDisposable
{
    readonly Computer _pc = new() { IsCpuEnabled = true, IsMemoryEnabled = true, IsGpuEnabled = true };
    readonly object _gate = new();
    DateTime _gpuUpdated = DateTime.MinValue;

    readonly Task _open;

    /// <summary>
    /// Opens the sensors in the background: on some PCs (for example with RGB software busy on the memory bus) this
    /// takes a long time, and it must never hold up the helper. Readings start once it's done.
    /// </summary>
    public LhmCpuMonitor() => _open = Task.Run(() =>
    {
        using var _ = Redline.Core.Diagnostics.HelperLog.Time("open sensors (LibreHardwareMonitor)");
        lock (_gate) _pc.Open();
    });

    bool Ready => _open.IsCompletedSuccessfully;

    /// <summary>Completes when the sensors are open and readings are available.</summary>
    public Task Opened => _open;

    static double? Val(IEnumerable<ISensor> s, SensorType t, params string[] names) =>
        names.Select(n => s.FirstOrDefault(x => x.SensorType == t && x.Name.Contains(n, StringComparison.OrdinalIgnoreCase))?.Value)
             .FirstOrDefault(v => v is not null);

    /// <summary>
    /// One value per physical core: names exactly like "Core #3" (clocks) or "CPU Core #3" (loads). LHM also reports
    /// "Core #3 (Effective)" and "CPU Core #3 Thread #1", which would double the list.
    /// </summary>
    public static List<double> PerCore(IEnumerable<(string Name, int Index, float? Value)> sensors, string prefix) =>
        sensors.Where(x => x.Value is not null && x.Name.StartsWith(prefix, StringComparison.Ordinal)
                           && int.TryParse(x.Name.AsSpan(prefix.Length), out _))
               .OrderBy(x => x.Index).Select(x => (double)x.Value!.Value).ToList();

    public CpuState? Read()
    {
        if (!Ready) return null;
        lock (_gate)
        {
            var cpu = _pc.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            if (cpu is null) return null;
            cpu.Update();
            var s = cpu.Sensors;
            var temp = Val(s, SensorType.Temperature, "Tctl/Tdie", "Package", "Tdie", "Core Max", "Core Average");
            var load = Val(s, SensorType.Load, "CPU Total") ?? 0;
            var coreClocks = PerCore(s.Where(x => x.SensorType == SensorType.Clock).Select(x => (x.Name, x.Index, x.Value)), "Core #")
                .Where(v => v > 0).ToList();
            var coreLoads = PerCore(s.Where(x => x.SensorType == SensorType.Load).Select(x => (x.Name, x.Index, x.Value)), "CPU Core #");
            var power = Val(s, SensorType.Power, "Package");

            double? ramUsed = null, ramTotal = null;
            var mem = _pc.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory);
            if (mem is not null)
            {
                mem.Update();
                ramUsed = Val(mem.Sensors, SensorType.Data, "Memory Used");
                var avail = Val(mem.Sensors, SensorType.Data, "Memory Available");
                if (ramUsed is { } u && avail is { } a) ramTotal = u + a;
            }

            return new CpuState(cpu.Name, temp, load, coreClocks.Count > 0 ? coreClocks.Average() : null, power, temp is null)
            {
                CoreClocksMhz = coreClocks, CoreLoadsPct = coreLoads, RamUsedGb = ramUsed, RamTotalGb = ramTotal,
            };
        }
    }

    public GpuExtras? Read(string gpuName)
    {
        if (!Ready) return null;
        lock (_gate)
        {
            var gpu = _pc.Hardware.FirstOrDefault(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel
                                                       && (gpuName.Contains(h.Name, StringComparison.OrdinalIgnoreCase) || h.Name.Contains(gpuName, StringComparison.OrdinalIgnoreCase)));
            if (gpu is null) return null;
            if (DateTime.UtcNow - _gpuUpdated > TimeSpan.FromMilliseconds(500)) { gpu.Update(); _gpuUpdated = DateTime.UtcNow; }
            var s = gpu.Sensors;
            var volt = Val(s, SensorType.Voltage, "GPU Core");
            return new GpuExtras(
                Val(s, SensorType.Temperature, "Hot Spot"),
                Val(s, SensorType.Temperature, "Memory Junction", "GPU Memory"),
                Val(s, SensorType.Fan, "GPU Fan", "Fan"),
                volt is { } v ? v * 1000 : null);
        }
    }

    public void Dispose()
    {
        try { _open.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
        if (Ready) lock (_gate) _pc.Close();
    }
}
