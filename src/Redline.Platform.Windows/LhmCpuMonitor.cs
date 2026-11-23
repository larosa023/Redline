using LibreHardwareMonitor.Hardware;
using Redline.Core.Cpu;

namespace Redline.Platform.Windows;

/// <summary>CPU readings via LibreHardwareMonitor. Without PawnIO, temperatures/power may be missing (SensorsLimited).</summary>
public sealed class LhmCpuMonitor : ICpuMonitor, IDisposable
{
    readonly Computer _pc = new() { IsCpuEnabled = true };
    readonly object _gate = new();

    public LhmCpuMonitor() => _pc.Open();

    public CpuState? Read()
    {
        lock (_gate)
        {
            var cpu = _pc.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            if (cpu is null) return null;
            cpu.Update();
            var s = cpu.Sensors;
            double? Val(SensorType t, params string[] names) =>
                names.Select(n => s.FirstOrDefault(x => x.SensorType == t && x.Name.Contains(n, StringComparison.OrdinalIgnoreCase))?.Value)
                     .FirstOrDefault(v => v is not null);
            var temp = Val(SensorType.Temperature, "Tctl/Tdie", "Package", "Tdie", "Core Max", "Core Average");
            var load = Val(SensorType.Load, "CPU Total") ?? 0;
            var clocks = s.Where(x => x.SensorType == SensorType.Clock && x.Name.StartsWith("Core #", StringComparison.Ordinal) && x.Value is > 0)
                          .Select(x => (double)x.Value!.Value).ToList();
            var power = Val(SensorType.Power, "Package");
            return new CpuState(cpu.Name, temp, load, clocks.Count > 0 ? clocks.Average() : null, power, temp is null);
        }
    }

    public void Dispose() => _pc.Close();
}
