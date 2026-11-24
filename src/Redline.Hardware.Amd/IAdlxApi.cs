namespace Redline.Hardware.Amd;

public sealed record AdlxRange(int Min, int Max, int Step);
public sealed record AdlxGpuInfo(int Index, string Name, string UniqueId, int TotalVramMb, string DriverVersion);
public sealed record AdlxSupport(bool Gfx, bool Gfx2, bool Vram, bool Power);
/// <summary>Driver ranges. GPU max frequency and VRAM max frequency in MHz, voltage in mV, power as % offset from 0.</summary>
public sealed record AdlxRanges(AdlxRange? GpuMaxFreq, AdlxRange? GpuVoltage, AdlxRange? VramMaxFreq, AdlxRange? PowerPct);
public sealed record AdlxValues(int? GpuMaxFreqMhz, int? GpuVoltageMv, int? VramMaxFreqMhz, int? PowerPct);
public sealed record AdlxMetrics(double UsagePct, int ClockMhz, int VramClockMhz, double TempC, double? HotspotC, double PowerW,
    int? FanRpm, int VramUsedMb, int? VoltageMv, double? BoardPowerW);

/// <summary>The few ADLX calls Redline needs, by GPU index. Native implementation: <see cref="AdlxApi"/>.</summary>
public interface IAdlxApi
{
    bool TryLoad();
    IReadOnlyList<AdlxGpuInfo> Gpus();
    AdlxSupport Support(int gpu);
    AdlxRanges Ranges(int gpu);
    bool IsAtFactory(int gpu);
    AdlxValues Read(int gpu);
    /// <summary>Writes the non-null values.</summary>
    void Write(int gpu, AdlxValues v);
    void ResetToFactory(int gpu);
    AdlxMetrics? Metrics(int gpu);
}

public sealed class AdlxException(string message) : Exception(message);
