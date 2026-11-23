using Redline.Hardware.Nvidia;

namespace Redline.Hardware.Tests.Nvidia;

/// <summary>In-memory NVML: one RTX 4090-like GPU unless configured otherwise.</summary>
public sealed class FakeNvml : INvmlApi
{
    public bool DllPresent = true;
    public bool NewOffsetsApi = true;          // R555+ nvmlDeviceSetClockOffsets
    public string Name = "NVIDIA GeForce RTX 4090";
    public uint DefaultMw = 450_000, MinMw = 150_000, MaxMw = 600_000, LimitMw = 450_000;
    public int CoreOffset, MemOffset, CoreMin = -1000, CoreMax = 1000, MemMin = -2000, MemMax = 3000;
    public int? DriverCoreCap;                 // driver silently caps core offset
    public uint Fans = 2, FanMin = 30, FanMax = 100;
    public uint[] FanPct = [0, 0];
    public bool[] FanManual = [false, false];
    public bool Lost;
    public List<string> Calls = new();

    public bool TryLoad() => DllPresent;
    public void Init() { Calls.Add("Init"); }
    public void Shutdown() { }
    public string DriverVersion() => "581.42";
    public int DeviceCount() => 1;
    public nint Handle(int index) => 1;
    public string DeviceName(nint h) => Name;
    public string Uuid(nint h) => "GPU-1234";
    public (uint MinMw, uint MaxMw) PowerLimitConstraints(nint h) => (MinMw, MaxMw);
    public uint PowerDefaultLimitMw(nint h) => DefaultMw;
    public uint PowerLimitMw(nint h) => LimitMw;
    public void SetPowerLimitMw(nint h, uint mw) { Calls.Add($"SetPower {mw}"); LimitMw = Math.Clamp(mw, MinMw, MaxMw); }

    public (int Min, int Max, int Current) GetClockOffsets(nint h, NvmlClockType t)
    {
        if (!NewOffsetsApi) throw new NvmlException(NvmlReturn.FunctionNotFound, "nvmlDeviceGetClockOffsets");
        return t == NvmlClockType.Graphics ? (CoreMin, CoreMax, CoreOffset) : (MemMin, MemMax, MemOffset);
    }
    public void SetClockOffsets(nint h, NvmlClockType t, int mhz)
    {
        if (!NewOffsetsApi) throw new NvmlException(NvmlReturn.FunctionNotFound, "nvmlDeviceSetClockOffsets");
        Calls.Add($"SetClockOffsets {t} {mhz}");
        Store(t, mhz);
    }
    public (int Min, int Max) GetVfOffsetRange(nint h, NvmlClockType t) => t == NvmlClockType.Graphics ? (CoreMin, CoreMax) : (MemMin, MemMax);
    public int GetVfOffset(nint h, NvmlClockType t) => t == NvmlClockType.Graphics ? CoreOffset : MemOffset;
    public void SetVfOffset(nint h, NvmlClockType t, int mhz) { Calls.Add($"SetVfOffset {t} {mhz}"); Store(t, mhz); }

    void Store(NvmlClockType t, int mhz)
    {
        if (t == NvmlClockType.Graphics) CoreOffset = DriverCoreCap is { } c ? Math.Min(c, mhz) : mhz;
        else MemOffset = mhz;
    }

    public uint FanCount(nint h) => Fans;
    public (uint Min, uint Max) FanMinMax(nint h) => (FanMin, FanMax);
    public void SetFanSpeed(nint h, uint fan, uint pct) { FanPct[fan] = pct; FanManual[fan] = true; }
    public void SetDefaultFanSpeed(nint h, uint fan) { FanManual[fan] = false; Calls.Add($"FanAuto {fan}"); }
    public uint FanSpeed(nint h, uint fan) => FanPct[fan];

    void CheckLost() { if (Lost) throw new NvmlException(NvmlReturn.GpuIsLost, "telemetry"); }
    public uint TemperatureC(nint h) { CheckLost(); return 61; }
    public uint ClockMhz(nint h, NvmlClockType t) { CheckLost(); return t == NvmlClockType.Graphics ? 2730u : 10501u; }
    public uint PowerUsageMw(nint h) { CheckLost(); return 380_000; }
    public (uint Gpu, uint Memory) Utilization(nint h) { CheckLost(); return (97, 60); }
    public (ulong Used, ulong Total) Memory(nint h) { CheckLost(); return (6UL << 30, 24UL << 30); }
}
