using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Redline.Hardware.Nvidia;

/// <summary>Real NVML via P/Invoke. nvml.dll ships with the NVIDIA driver; nothing is bundled.</summary>
public sealed class NvmlApi : INvmlApi
{
    const string Lib = "nvml";
    static nint _lib;
    static bool _resolverSet;

    public bool TryLoad()
    {
        if (_lib != 0) return true;
        var candidates = OperatingSystem.IsWindows()
            ? new[]
            {
                Path.Combine(Environment.SystemDirectory, "nvml.dll"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll"),
            }
            : new[] { "libnvidia-ml.so.1" };
        foreach (var c in candidates)
            if (NativeLibrary.TryLoad(c, out _lib)) break;
        if (_lib == 0) return false;
        if (!_resolverSet)
        {
            NativeLibrary.SetDllImportResolver(typeof(NvmlApi).Assembly, (name, _, _) => name == Lib ? _lib : 0);
            _resolverSet = true;
        }
        return true;
    }

    static void Check(int r, string fn)
    {
        if (r != 0) throw new NvmlException(Enum.IsDefined(typeof(NvmlReturn), r) ? (NvmlReturn)r : NvmlReturn.Unknown, fn);
    }

    static T Call<T>(Func<T> f, string fn)
    {
        try { return f(); }
        catch (EntryPointNotFoundException) { throw new NvmlException(NvmlReturn.FunctionNotFound, fn); }
    }

    static void Call(Action f, string fn)
    {
        try { f(); }
        catch (EntryPointNotFoundException) { throw new NvmlException(NvmlReturn.FunctionNotFound, fn); }
    }

    static string Str(byte[] b) => Encoding.ASCII.GetString(b, 0, Math.Max(0, Array.IndexOf(b, (byte)0) is var i && i >= 0 ? i : b.Length));

    public void Init() => Call(() => Check(N.nvmlInit_v2(), "nvmlInit_v2"), "nvmlInit_v2");
    public void Shutdown() => Call(() => N.nvmlShutdown(), "nvmlShutdown");

    public string DriverVersion()
    {
        var b = new byte[96];
        Check(N.nvmlSystemGetDriverVersion(b, (uint)b.Length), "nvmlSystemGetDriverVersion");
        return Str(b);
    }

    public int DeviceCount() { Check(N.nvmlDeviceGetCount_v2(out var c), "nvmlDeviceGetCount_v2"); return (int)c; }
    public nint Handle(int index) { Check(N.nvmlDeviceGetHandleByIndex_v2((uint)index, out var h), "nvmlDeviceGetHandleByIndex_v2"); return h; }

    public string DeviceName(nint h) { var b = new byte[96]; Check(N.nvmlDeviceGetName(h, b, (uint)b.Length), "nvmlDeviceGetName"); return Str(b); }
    public string Uuid(nint h) { var b = new byte[96]; Check(N.nvmlDeviceGetUUID(h, b, (uint)b.Length), "nvmlDeviceGetUUID"); return Str(b); }

    public (uint MinMw, uint MaxMw) PowerLimitConstraints(nint h)
    {
        Check(N.nvmlDeviceGetPowerManagementLimitConstraints(h, out var min, out var max), "nvmlDeviceGetPowerManagementLimitConstraints");
        return (min, max);
    }
    public uint PowerDefaultLimitMw(nint h) { Check(N.nvmlDeviceGetPowerManagementDefaultLimit(h, out var v), "nvmlDeviceGetPowerManagementDefaultLimit"); return v; }
    public uint PowerLimitMw(nint h) { Check(N.nvmlDeviceGetPowerManagementLimit(h, out var v), "nvmlDeviceGetPowerManagementLimit"); return v; }
    public void SetPowerLimitMw(nint h, uint mw) => Check(N.nvmlDeviceSetPowerManagementLimit(h, mw), "nvmlDeviceSetPowerManagementLimit");

    static N.ClockOffset Offset(NvmlClockType t) => new()
    {
        Version = (uint)Marshal.SizeOf<N.ClockOffset>() | (1u << 24),
        Type = (int)t,
        Pstate = 0,
    };

    public (int Min, int Max, int Current) GetClockOffsets(nint h, NvmlClockType t)
    {
        var o = Offset(t);
        Call(() => Check(N.nvmlDeviceGetClockOffsets(h, ref o), "nvmlDeviceGetClockOffsets"), "nvmlDeviceGetClockOffsets");
        return (o.MinClockOffsetMHz, o.MaxClockOffsetMHz, o.ClockOffsetMHz);
    }

    public void SetClockOffsets(nint h, NvmlClockType t, int mhz)
    {
        var o = Offset(t);
        o.ClockOffsetMHz = mhz;
        Call(() => Check(N.nvmlDeviceSetClockOffsets(h, ref o), "nvmlDeviceSetClockOffsets"), "nvmlDeviceSetClockOffsets");
    }

    public (int Min, int Max) GetVfOffsetRange(nint h, NvmlClockType t)
    {
        int min = 0, max = 0;
        if (t == NvmlClockType.Graphics) Call(() => Check(N.nvmlDeviceGetGpcClkMinMaxVfOffset(h, out min, out max), "nvmlDeviceGetGpcClkMinMaxVfOffset"), "GpcMinMax");
        else Call(() => Check(N.nvmlDeviceGetMemClkMinMaxVfOffset(h, out min, out max), "nvmlDeviceGetMemClkMinMaxVfOffset"), "MemMinMax");
        return (min, max);
    }

    public int GetVfOffset(nint h, NvmlClockType t)
    {
        var v = 0;
        if (t == NvmlClockType.Graphics) Call(() => Check(N.nvmlDeviceGetGpcClkVfOffset(h, out v), "nvmlDeviceGetGpcClkVfOffset"), "GpcGet");
        else Call(() => Check(N.nvmlDeviceGetMemClkVfOffset(h, out v), "nvmlDeviceGetMemClkVfOffset"), "MemGet");
        return v;
    }

    public void SetVfOffset(nint h, NvmlClockType t, int mhz)
    {
        if (t == NvmlClockType.Graphics) Call(() => Check(N.nvmlDeviceSetGpcClkVfOffset(h, mhz), "nvmlDeviceSetGpcClkVfOffset"), "GpcSet");
        else Call(() => Check(N.nvmlDeviceSetMemClkVfOffset(h, mhz), "nvmlDeviceSetMemClkVfOffset"), "MemSet");
    }

    public uint FanCount(nint h) { Check(N.nvmlDeviceGetNumFans(h, out var n), "nvmlDeviceGetNumFans"); return n; }
    public (uint Min, uint Max) FanMinMax(nint h) { Check(N.nvmlDeviceGetMinMaxFanSpeed(h, out var a, out var b), "nvmlDeviceGetMinMaxFanSpeed"); return (a, b); }
    public void SetFanSpeed(nint h, uint fan, uint pct) => Check(N.nvmlDeviceSetFanSpeed_v2(h, fan, pct), "nvmlDeviceSetFanSpeed_v2");
    public void SetDefaultFanSpeed(nint h, uint fan) => Check(N.nvmlDeviceSetDefaultFanSpeed_v2(h, fan), "nvmlDeviceSetDefaultFanSpeed_v2");
    public uint FanSpeed(nint h, uint fan) { Check(N.nvmlDeviceGetFanSpeed_v2(h, fan, out var v), "nvmlDeviceGetFanSpeed_v2"); return v; }
    public uint TemperatureC(nint h) { Check(N.nvmlDeviceGetTemperature(h, 0, out var v), "nvmlDeviceGetTemperature"); return v; }
    public uint ClockMhz(nint h, NvmlClockType t) { Check(N.nvmlDeviceGetClockInfo(h, (int)t, out var v), "nvmlDeviceGetClockInfo"); return v; }
    public uint PowerUsageMw(nint h) { Check(N.nvmlDeviceGetPowerUsage(h, out var v), "nvmlDeviceGetPowerUsage"); return v; }
    public (uint Gpu, uint Memory) Utilization(nint h) { Check(N.nvmlDeviceGetUtilizationRates(h, out var u), "nvmlDeviceGetUtilizationRates"); return (u.Gpu, u.Memory); }
    public (ulong Used, ulong Total) Memory(nint h) { Check(N.nvmlDeviceGetMemoryInfo(h, out var m), "nvmlDeviceGetMemoryInfo"); return (m.Used, m.Total); }

    static class N
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct ClockOffset { public uint Version; public int Type; public int Pstate; public int ClockOffsetMHz; public int MinClockOffsetMHz; public int MaxClockOffsetMHz; }
        [StructLayout(LayoutKind.Sequential)] public struct Utilization { public uint Gpu; public uint Memory; }
        [StructLayout(LayoutKind.Sequential)] public struct MemoryInfo { public ulong Total; public ulong Free; public ulong Used; }

        [DllImport(Lib)] public static extern int nvmlInit_v2();
        [DllImport(Lib)] public static extern int nvmlShutdown();
        [DllImport(Lib)] public static extern int nvmlSystemGetDriverVersion(byte[] v, uint len);
        [DllImport(Lib)] public static extern int nvmlDeviceGetCount_v2(out uint c);
        [DllImport(Lib)] public static extern int nvmlDeviceGetHandleByIndex_v2(uint i, out nint h);
        [DllImport(Lib)] public static extern int nvmlDeviceGetName(nint h, byte[] n, uint len);
        [DllImport(Lib)] public static extern int nvmlDeviceGetUUID(nint h, byte[] n, uint len);
        [DllImport(Lib)] public static extern int nvmlDeviceGetPowerManagementLimitConstraints(nint h, out uint min, out uint max);
        [DllImport(Lib)] public static extern int nvmlDeviceGetPowerManagementDefaultLimit(nint h, out uint v);
        [DllImport(Lib)] public static extern int nvmlDeviceGetPowerManagementLimit(nint h, out uint v);
        [DllImport(Lib)] public static extern int nvmlDeviceSetPowerManagementLimit(nint h, uint v);
        [DllImport(Lib)] public static extern int nvmlDeviceGetClockOffsets(nint h, ref ClockOffset o);
        [DllImport(Lib)] public static extern int nvmlDeviceSetClockOffsets(nint h, ref ClockOffset o);
        [DllImport(Lib)] public static extern int nvmlDeviceGetGpcClkMinMaxVfOffset(nint h, out int min, out int max);
        [DllImport(Lib)] public static extern int nvmlDeviceGetMemClkMinMaxVfOffset(nint h, out int min, out int max);
        [DllImport(Lib)] public static extern int nvmlDeviceGetGpcClkVfOffset(nint h, out int v);
        [DllImport(Lib)] public static extern int nvmlDeviceGetMemClkVfOffset(nint h, out int v);
        [DllImport(Lib)] public static extern int nvmlDeviceSetGpcClkVfOffset(nint h, int v);
        [DllImport(Lib)] public static extern int nvmlDeviceSetMemClkVfOffset(nint h, int v);
        [DllImport(Lib)] public static extern int nvmlDeviceGetNumFans(nint h, out uint n);
        [DllImport(Lib)] public static extern int nvmlDeviceGetMinMaxFanSpeed(nint h, out uint min, out uint max);
        [DllImport(Lib)] public static extern int nvmlDeviceSetFanSpeed_v2(nint h, uint fan, uint speed);
        [DllImport(Lib)] public static extern int nvmlDeviceSetDefaultFanSpeed_v2(nint h, uint fan);
        [DllImport(Lib)] public static extern int nvmlDeviceGetFanSpeed_v2(nint h, uint fan, out uint speed);
        [DllImport(Lib)] public static extern int nvmlDeviceGetTemperature(nint h, int sensor, out uint t);
        [DllImport(Lib)] public static extern int nvmlDeviceGetClockInfo(nint h, int type, out uint mhz);
        [DllImport(Lib)] public static extern int nvmlDeviceGetPowerUsage(nint h, out uint mw);
        [DllImport(Lib)] public static extern int nvmlDeviceGetUtilizationRates(nint h, out Utilization u);
        [DllImport(Lib)] public static extern int nvmlDeviceGetMemoryInfo(nint h, out MemoryInfo m);
    }
}
