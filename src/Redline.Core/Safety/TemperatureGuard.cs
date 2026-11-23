using Redline.Core.Gpu;

namespace Redline.Core.Safety;

public abstract record GuardAction
{
    public sealed record None : GuardAction;
    public sealed record ScaleBack(TuneSettings Reduced) : GuardAction;
    public sealed record RevertToFactory(string Reason) : GuardAction;
}

/// <summary>
/// Watches GPU temperature while a tune is applied. There is deliberately no way to disable it.
/// 5 samples at/over the ceiling → scale back; any sample ≥ ceiling + 5 °C → factory; 5 missing samples → factory.
/// </summary>
public sealed class TemperatureGuard(int ceilingC = 83)
{
    public const int MinCeilingC = 70, MaxCeilingC = 90;
    const int Samples = 5, HardMarginC = 5;

    int _ceiling = Check(ceilingC);
    int _hot, _missing;

    public int CeilingC { get => _ceiling; set => _ceiling = Check(value); }

    static int Check(int c) => c is < MinCeilingC or > MaxCeilingC
        ? throw new ArgumentOutOfRangeException(nameof(CeilingC), c, $"Ceiling must be {MinCeilingC}–{MaxCeilingC} °C.")
        : c;

    public GuardAction Evaluate(Telemetry? t, TuneSettings active)
    {
        if (active.IsFactory) { _hot = _missing = 0; return new GuardAction.None(); }

        if (t is null)
        {
            if (++_missing < Samples) return new GuardAction.None();
            _hot = _missing = 0;
            return new GuardAction.RevertToFactory("GPU readings were lost while a tune was applied.");
        }
        _missing = 0;

        if (t.TempC >= _ceiling + HardMarginC)
        {
            _hot = 0;
            return new GuardAction.RevertToFactory($"GPU reached {t.TempC:0} °C.");
        }
        if (t.TempC < _ceiling) { _hot = 0; return new GuardAction.None(); }
        if (++_hot < Samples) return new GuardAction.None();
        _hot = 0;
        return new GuardAction.ScaleBack(Reduce(active));
    }

    static TuneSettings Reduce(TuneSettings s) => s with
    {
        CoreOffsetMhz = s.CoreOffsetMhz / 2,
        MemoryOffsetMhz = s.MemoryOffsetMhz / 2,
        PowerLimitPct = s.PowerLimitPct is { } p ? Math.Min(p, 100) : null,
        VoltageOffsetMv = s.VoltageOffsetMv is { } v ? Math.Min(v, 0) : null,
    };
}
