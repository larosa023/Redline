namespace Redline.Hardware.Intel;

/// <summary>IGCL ctl_units_t values Redline understands.</summary>
public enum IgclUnits { MHz = 0, Volts = 3, Watts = 4, Celsius = 5, Joules = 6, Seconds = 7, Rpm = 9, MilliWatts = 10, Percent = 11, MilliVolts = 13 }

public sealed record IgclControl(double Min, double Max, double Step, double Default, IgclUnits Units);
public sealed record IgclGpuInfo(int Index, string Name, string DriverVersion, string Id);
public sealed record IgclOc(bool Supported, IgclControl? GpuFrequencyOffset, IgclControl? GpuVoltageOffset, IgclControl? VramFrequencyOffset, IgclControl? PowerLimit);
public sealed record IgclTelemetry(double? CoreClockMhz, double? MemClockMhz, double? TempC, double? MemTempC, double? PowerW, double? UsagePct,
    double? FanRpm, double? VoltageMv, bool PowerLimited, bool TempLimited, bool VoltageLimited, bool UtilLimited);

/// <summary>The IGCL (ControlLib.dll) calls Redline needs, by GPU index. Native implementation: <see cref="IgclApi"/>.</summary>
public interface IIgclApi
{
    bool TryLoad();
    IReadOnlyList<IgclGpuInfo> Gpus();
    IgclOc Properties(int gpu);
    void AcceptWaiver(int gpu);
    double GetGpuFrequencyOffset(int gpu);
    void SetGpuFrequencyOffset(int gpu, double mhz);
    double GetGpuVoltageOffset(int gpu);
    void SetGpuVoltageOffset(int gpu, double mv);
    double GetVramFrequencyOffset(int gpu);
    void SetVramFrequencyOffset(int gpu, double mhz);
    /// <summary>Sustained power limit, in the units of <see cref="IgclOc.PowerLimit"/>.</summary>
    double GetPowerLimit(int gpu);
    void SetPowerLimit(int gpu, double value);
    void ResetToDefault(int gpu);
    IgclTelemetry? Telemetry(int gpu);
}

public sealed class IgclException(string message) : Exception(message);
