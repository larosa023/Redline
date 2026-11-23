namespace Redline.Hardware.Nvidia;

public enum NvmlReturn
{
    Success = 0, Uninitialized = 1, InvalidArgument = 2, NotSupported = 3, NoPermission = 4, AlreadyInitialized = 5,
    NotFound = 6, InsufficientSize = 7, InsufficientPower = 8, DriverNotLoaded = 9, Timeout = 10, IrqIssue = 11,
    LibraryNotFound = 12, FunctionNotFound = 13, CorruptedInforom = 14, GpuIsLost = 15, ResetRequired = 16,
    OperatingSystem = 17, LibRmVersionMismatch = 18, InUse = 19, Memory = 20, NoData = 21, Unknown = 999,
}

public sealed class NvmlException(NvmlReturn code, string function) : Exception($"{function} failed: {code}")
{
    public NvmlReturn Code { get; } = code;
    public bool Unsupported => Code is NvmlReturn.NotSupported or NvmlReturn.FunctionNotFound;
}

public enum NvmlClockType { Graphics = 0, Sm = 1, Memory = 2 }

/// <summary>The slice of NVML Redline uses. Methods throw <see cref="NvmlException"/> on failure.</summary>
public interface INvmlApi
{
    bool TryLoad();
    void Init();
    void Shutdown();
    string DriverVersion();
    int DeviceCount();
    nint Handle(int index);
    string DeviceName(nint h);
    string Uuid(nint h);
    (uint MinMw, uint MaxMw) PowerLimitConstraints(nint h);
    uint PowerDefaultLimitMw(nint h);
    uint PowerLimitMw(nint h);
    void SetPowerLimitMw(nint h, uint mw);
    /// <summary>R555+ API, P0 state.</summary>
    (int Min, int Max, int Current) GetClockOffsets(nint h, NvmlClockType t);
    void SetClockOffsets(nint h, NvmlClockType t, int mhz);
    /// <summary>Older Gpc/Mem VF offset API.</summary>
    (int Min, int Max) GetVfOffsetRange(nint h, NvmlClockType t);
    int GetVfOffset(nint h, NvmlClockType t);
    void SetVfOffset(nint h, NvmlClockType t, int mhz);
    uint FanCount(nint h);
    (uint Min, uint Max) FanMinMax(nint h);
    void SetFanSpeed(nint h, uint fan, uint pct);
    void SetDefaultFanSpeed(nint h, uint fan);
    uint FanSpeed(nint h, uint fan);
    uint TemperatureC(nint h);
    uint ClockMhz(nint h, NvmlClockType t);
    uint PowerUsageMw(nint h);
    (uint Gpu, uint Memory) Utilization(nint h);
    (ulong Used, ulong Total) Memory(nint h);
}
