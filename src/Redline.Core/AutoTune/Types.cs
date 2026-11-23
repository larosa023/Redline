using Redline.Core.Gpu;

namespace Redline.Core.AutoTune;

public sealed record StressResult(bool Completed, long Errors, double Score, bool DriverReset, double AvgPowerW, double MaxTempC)
{
    public bool Stable => Completed && Errors == 0 && !DriverReset;
}

public interface IStressRunner
{
    Task<StressResult> RunAsync(IGpuDevice d, TimeSpan duration, CancellationToken ct);
}

public enum AutoTuneMode { Performance, Efficiency }

public sealed record AutoTuneOptions(AutoTuneMode Mode, bool AllowPowerAbove100 = false,
    TimeSpan? StepDuration = null, TimeSpan? FinalDuration = null,
    int CoreStepMhz = 15, int MemStepMhz = 50, int CoreMarginSteps = 2, int MemMarginSteps = 1,
    double EfficiencyTolerance = 0.02, int PowerStepPct = 5, int VoltageStepMv = 10)
{
    public TimeSpan Step => StepDuration ?? TimeSpan.FromSeconds(60);
    public TimeSpan Final => FinalDuration ?? TimeSpan.FromMinutes(5);
}

public enum AutoTuneOutcome { Completed, Cancelled, FailedAtBaseline }

public sealed record AutoTuneStep(string Label, TuneSettings Settings, StressResult Result, bool Passed);

public sealed record AutoTuneProgress(double Fraction, string Label, TuneSettings Testing, TimeSpan EstimatedRemaining);

public sealed record AutoTuneResult(AutoTuneOutcome Outcome, TuneSettings Best, double BaselineScore, double FinalScore,
    double BaselinePowerW, double FinalPowerW, IReadOnlyList<AutoTuneStep> Log);
