using System.Text.Json;
using Redline.Core.Storage;

namespace Redline.Core.Cpu;

/// <summary>Values match Windows' PERFBOOSTMODE power setting.</summary>
public enum CpuBoostMode { Disabled = 0, Enabled = 1, Aggressive = 2, EfficientEnabled = 3, EfficientAggressive = 4 }

/// <summary>CPU behaviour set through Windows' own power plan. Null fields are left unchanged.</summary>
public sealed record CpuBoostSettings(CpuBoostMode? BoostMode, int? MaxProcessorStatePct)
{
    public static CpuBoostSettings Whisper { get; } = new(CpuBoostMode.Disabled, null);
    public static CpuBoostSettings SendIt { get; } = new(CpuBoostMode.Aggressive, null);
}

public interface IPowerPlan
{
    CpuBoostSettings ReadCurrent();
    void Apply(CpuBoostSettings s);
}

public sealed record CpuState(string Name, double? PackageTempC, double UsagePct, double? EffectiveClockMhz, double? PackagePowerW, bool SensorsLimited)
{
    public IReadOnlyList<double> CoreClocksMhz { get; init; } = [];
    public IReadOnlyList<double> CoreLoadsPct { get; init; } = [];
    public double? RamUsedGb { get; init; }
    public double? RamTotalGb { get; init; }
}

public interface ICpuMonitor
{
    CpuState? Read();
}

/// <summary>Applies profile CPU settings, remembering the user's original Windows values (on disk) so Factory restores them.</summary>
public sealed class CpuBoostController(IPowerPlan plan, string originalStatePath)
{
    public void Apply(CpuBoostSettings? s)
    {
        if (s is null) { RestoreOriginal(); return; }
        if (s.MaxProcessorStatePct is { } m and (< 5 or > 100))
            throw new ArgumentOutOfRangeException(nameof(s), m, "Maximum processor state must be 5–100%.");
        if (!File.Exists(originalStatePath))
            Json.WriteAtomic(originalStatePath, JsonSerializer.Serialize(plan.ReadCurrent(), Json.Options));
        plan.Apply(s);
    }

    public void RestoreOriginal()
    {
        if (!File.Exists(originalStatePath)) return;
        var orig = JsonSerializer.Deserialize<CpuBoostSettings>(File.ReadAllText(originalStatePath), Json.Options);
        if (orig is not null) plan.Apply(orig);
        File.Delete(originalStatePath);
    }
}
