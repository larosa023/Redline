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

    static double? Val(IEnumerable<ISensor> s, SensorType t, params string[] names) =>
        names.Select(n => s.FirstOrDefault(x => x.SensorType == t && x.Name.Contains(n, StringComparison.OrdinalIgnoreCase))?.Value)
             .FirstOrDefault(v => v is not null);

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
            var coreClocks = s.Where(x => x.SensorType == SensorType.Clock && x.Name.StartsWith("Core #", StringComparison.Ordinal) && x.Value is > 0)
                              .OrderBy(x => x.Index).Select(x => (double)x.Value!.Value).ToList();
            var coreLoads = s.Where(x => x.SensorType == SensorType.Load && x.Name.StartsWith("CPU Core #", StringComparison.Ordinal) && x.Value is not null)
                             .OrderBy(x => x.Index).Select(x => (double)x.Value!.Value).ToList();
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
