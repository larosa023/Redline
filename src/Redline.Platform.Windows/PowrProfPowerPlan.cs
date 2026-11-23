using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Redline.Core.Cpu;

namespace Redline.Platform.Windows;

/// <summary>CPU boost settings through Windows' own power plan (no drivers). Writes AC and DC values of the active scheme.</summary>
[SupportedOSPlatform("windows")]
public sealed class PowrProfPowerPlan : IPowerPlan
{
    static Guid _subProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
    static Guid _boostMode = new("be337238-0d82-4146-a960-4f3749d470c7");
    static Guid _maxState = new("bc5038f7-23e0-4960-96da-33abaf5935ec");

    static Guid ActiveScheme()
    {
        var r = PowerGetActiveScheme(0, out var p);
        if (r != 0) throw new InvalidOperationException($"PowerGetActiveScheme failed: {r}");
        try { return Marshal.PtrToStructure<Guid>(p); }
        finally { LocalFree(p); }
    }

    public CpuBoostSettings ReadCurrent()
    {
        var scheme = ActiveScheme();
        var r1 = PowerReadACValueIndex(0, ref scheme, ref _subProcessor, ref _boostMode, out var boost);
        var r2 = PowerReadACValueIndex(0, ref scheme, ref _subProcessor, ref _maxState, out var max);
        return new CpuBoostSettings(r1 == 0 ? (CpuBoostMode)Math.Min(boost, 4u) : null, r2 == 0 ? (int)max : null);
    }

    public void Apply(CpuBoostSettings s)
    {
        var scheme = ActiveScheme();
        if (s.BoostMode is { } b) Write(ref scheme, ref _boostMode, (uint)b);
        if (s.MaxProcessorStatePct is { } m) Write(ref scheme, ref _maxState, (uint)m);
        var r = PowerSetActiveScheme(0, ref scheme);
        if (r != 0) throw new InvalidOperationException($"PowerSetActiveScheme failed: {r}");
    }

    static void Write(ref Guid scheme, ref Guid setting, uint v)
    {
        var r = PowerWriteACValueIndex(0, ref scheme, ref _subProcessor, ref setting, v);
        if (r != 0) throw new InvalidOperationException($"PowerWriteACValueIndex failed: {r}");
        r = PowerWriteDCValueIndex(0, ref scheme, ref _subProcessor, ref setting, v);
        if (r != 0) throw new InvalidOperationException($"PowerWriteDCValueIndex failed: {r}");
    }

    [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(nint root, out nint guid);
    [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(nint root, ref Guid scheme);
    [DllImport("powrprof.dll")] static extern uint PowerReadACValueIndex(nint root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] static extern uint PowerWriteACValueIndex(nint root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] static extern uint PowerWriteDCValueIndex(nint root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
    [DllImport("kernel32.dll")] static extern nint LocalFree(nint p);
}
