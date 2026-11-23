using System.Reflection;
using System.Runtime.Versioning;
using Redline.Core.Gpu;
using Redline.Core.Sim;
using Redline.Core.Stress;
using Redline.Engine;
using Redline.Hardware.Nvidia;
using Redline.Platform.Windows;
using Redline.Stress;

namespace Redline.Helper;

[SupportedOSPlatform("windows")]
public static class Composition
{
    public static string Version => typeof(Composition).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    /// <summary>The real hardware engine. demo=true uses a simulated GPU (for the app's --demo mode and tests).</summary>
    public static Engine.Engine CreateEngine(string dataDir, bool demo, List<IDisposable> owned)
    {
        IEnumerable<IGpuBackend> backends = demo
            ? [new SimGpuBackend(new SimGpuDevice(new SimGpuSpec("Redline Demo GPU", DemoCaps(), 180, 1100, -60, 1000, 350), "demo-0"))]
            : [new NvidiaBackend(new NvmlApi())];
        var cpu = new LhmCpuMonitor(); owned.Add(cpu);
        IProcessWatcher procs = (IProcessWatcher?)TryCreate(() => new WmiProcessWatcher(), owned) ?? new NoProcesses();
        IStabilitySignals signals = (IStabilitySignals?)TryCreate(() => new EventLogStabilitySignals(), owned) ?? new NoSignals();
        return new Engine.Engine(new EngineOptions(dataDir, HelperVersion: Version), backends,
            new D3D11StressRunner(new GoldenStore(dataDir), forceWarp: demo), new PowrProfPowerPlan(), cpu, procs, signals,
            TimeProvider.System, PawnIoDetector.IsInstalled);
    }

    static T? TryCreate<T>(Func<T> f, List<IDisposable> owned) where T : class, IDisposable
    {
        try { var t = f(); owned.Add(t); return t; } catch (Exception) { return null; }
    }

    static Capabilities DemoCaps() => new(new Dictionary<Control, ControlRange>
    {
        [Control.PowerLimit] = new(50, 120, 1, 100),
        [Control.CoreOffset] = new(-200, 500, 15, 0),
        [Control.MemoryOffset] = new(-500, 2000, 50, 0),
        [Control.FanSpeed] = new(30, 100, 1, 0),
    }, HasFanCurve: true, Has16PinConnector: true);

    sealed class NoProcesses : IProcessWatcher { public event Action<string>? Started { add { } remove { } } public event Action<string>? Stopped { add { } remove { } } }
    sealed class NoSignals : IStabilitySignals { public event Action<string>? DriverReset { add { } remove { } } }
}
