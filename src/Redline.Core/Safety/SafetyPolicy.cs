using Redline.Core.Gpu;

namespace Redline.Core.Safety;

public sealed record SafetyOptions(bool Advanced, bool AllowPowerAbove100, bool ConnectorWarningAcknowledged)
{
    public static SafetyOptions Default { get; } = new(false, false, false);
}

public enum SafetyIssue { ClampedToSafeRange, Unsupported, PowerAbove100NotAllowed, ConnectorWarningRequired }

public sealed record ValidationResult(TuneSettings Clamped, IReadOnlyList<SafetyIssue> Issues)
{
    public bool Ok => Issues.Count == 0;
}

/// <summary>Redline's safety rules for any requested tune. The helper applies this to every request.</summary>
public static class SafetyPolicy
{
    const double SafeOffsetFraction = 0.6;

    /// <summary>Allowed range for a control. Non-advanced: offsets capped at 60% of the driver max, no voltage increase, power ≤ 100%.</summary>
    public static ControlRange SafeRange(Control c, ControlRange driver, bool advanced)
    {
        if (c == Control.PowerLimit) return driver with { Max = Math.Min(driver.Max, Math.Max(driver.Min, 100)) };
        if (advanced) return driver;
        return c switch
        {
            Control.CoreOffset or Control.MemoryOffset => driver with { Max = Math.Max(driver.Min, RoundDown(driver.Max * SafeOffsetFraction, driver.Step)) },
            Control.VoltageOffset => driver with { Max = Math.Max(driver.Min, Math.Min(driver.Max, 0)) },
            _ => driver,
        };
    }

    static int RoundDown(double v, int step) => step <= 1 ? (int)Math.Floor(v) : (int)Math.Floor(v / step) * step;

    public static ValidationResult Validate(TuneSettings s, Capabilities caps, SafetyOptions o)
    {
        var issues = new List<SafetyIssue>();
        var outS = s;
        foreach (var c in TuneSettings.ScalarControls)
        {
            var v = s.Get(c);
            if (v is null) continue;
            var driver = caps.Range(c);
            if (driver is null) { outS = outS.With(c, null); issues.Add(SafetyIssue.Unsupported); continue; }

            if (c == Control.PowerLimit && v.Value > 100)
            {
                if (!o.AllowPowerAbove100) { outS = outS.With(c, Math.Min(100, driver.Max)); issues.Add(SafetyIssue.PowerAbove100NotAllowed); continue; }
                if (caps.Has16PinConnector && !o.ConnectorWarningAcknowledged) { outS = outS.With(c, Math.Min(100, driver.Max)); issues.Add(SafetyIssue.ConnectorWarningRequired); continue; }
                var p = driver.Clamp(v.Value);
                if (p != v.Value) issues.Add(SafetyIssue.ClampedToSafeRange);
                outS = outS.With(c, p);
                continue;
            }

            var range = SafeRange(c, driver, o.Advanced);
            var x = range.Clamp(v.Value);
            if (x != v.Value) issues.Add(SafetyIssue.ClampedToSafeRange);
            outS = outS.With(c, x);
        }
        if (s.Fan is not null && !caps.HasFanCurve) { outS = outS with { Fan = null }; issues.Add(SafetyIssue.Unsupported); }
        return new ValidationResult(outS, issues.Distinct().ToList());
    }
}
