using Redline.Core.Gpu;
using Redline.Core.Safety;
using Redline.Core.Tests.Fakes;
using Xunit;

namespace Redline.Core.Tests.Safety;

public class SafetyPolicyTests
{
    static readonly SafetyOptions Normal = new(Advanced: false, AllowPowerAbove100: false, ConnectorWarningAcknowledged: false);

    [Fact]
    public void SafeRange_CoreOffset_Is60PercentOfDriverMax()
    {
        var r = SafetyPolicy.SafeRange(Control.CoreOffset, new ControlRange(0, 500, 15, 0), advanced: false);
        Assert.Equal(300, r.Max);
        Assert.Equal(0, r.Min);
    }

    [Fact]
    public void SafeRange_RoundsDownToStep()
    {
        var r = SafetyPolicy.SafeRange(Control.MemoryOffset, new ControlRange(0, 1000, 50, 0), advanced: false);
        Assert.Equal(600, r.Max);
        var r2 = SafetyPolicy.SafeRange(Control.CoreOffset, new ControlRange(0, 310, 15, 0), advanced: false);
        Assert.Equal(180, r2.Max); // 60% of 310 = 186 → 180
    }

    [Fact]
    public void SafeRange_Voltage_NoIncrease()
    {
        var r = SafetyPolicy.SafeRange(Control.VoltageOffset, new ControlRange(-150, 100, 5, 0), advanced: false);
        Assert.Equal(0, r.Max);
        Assert.Equal(-150, r.Min);
    }

    [Fact]
    public void SafeRange_Advanced_IsDriverRange()
    {
        var d = new ControlRange(-200, 500, 15, 0);
        Assert.Equal(d, SafetyPolicy.SafeRange(Control.CoreOffset, d, advanced: true));
    }

    [Fact]
    public void Validate_PowerAbove100_WithoutOptIn_Clamped()
    {
        var v = SafetyPolicy.Validate(TuneSettings.Factory with { PowerLimitPct = 110 }, TestGpus.Caps(), Normal);
        Assert.Equal(100, v.Clamped.PowerLimitPct);
        Assert.Equal([SafetyIssue.PowerAbove100NotAllowed], v.Issues);
        Assert.False(v.Ok);
    }

    [Fact]
    public void Validate_PowerAbove100_16Pin_NeedsAck()
    {
        var v = SafetyPolicy.Validate(TuneSettings.Factory with { PowerLimitPct = 110 }, TestGpus.Caps(sixteenPin: true),
            Normal with { AllowPowerAbove100 = true });
        Assert.Equal(100, v.Clamped.PowerLimitPct);
        Assert.Equal([SafetyIssue.ConnectorWarningRequired], v.Issues);
    }

    [Fact]
    public void Validate_PowerAbove100_16Pin_Acked_Allowed()
    {
        var v = SafetyPolicy.Validate(TuneSettings.Factory with { PowerLimitPct = 110 }, TestGpus.Caps(sixteenPin: true),
            Normal with { AllowPowerAbove100 = true, ConnectorWarningAcknowledged = true });
        Assert.Equal(110, v.Clamped.PowerLimitPct);
        Assert.True(v.Ok);
    }

    [Fact]
    public void Validate_PowerAbove100_StillCappedAtDriverMax()
    {
        var v = SafetyPolicy.Validate(TuneSettings.Factory with { PowerLimitPct = 150 }, TestGpus.Caps(),
            Normal with { AllowPowerAbove100 = true });
        Assert.Equal(133, v.Clamped.PowerLimitPct);
        Assert.Equal([SafetyIssue.ClampedToSafeRange], v.Issues);
    }

    [Fact]
    public void Validate_CoreAboveSafeRange_Clamped()
    {
        var v = SafetyPolicy.Validate(TuneSettings.Factory with { CoreOffsetMhz = 450 }, TestGpus.Caps(coreMax: 500), Normal);
        Assert.Equal(300, v.Clamped.CoreOffsetMhz);
        Assert.Equal([SafetyIssue.ClampedToSafeRange], v.Issues);
    }

    [Fact]
    public void Validate_Advanced_UsesDriverRange()
    {
        var v = SafetyPolicy.Validate(TuneSettings.Factory with { CoreOffsetMhz = 450 }, TestGpus.Caps(coreMax: 500),
            Normal with { Advanced = true });
        Assert.Equal(450, v.Clamped.CoreOffsetMhz);
        Assert.True(v.Ok);
    }

    [Fact]
    public void Validate_Unsupported_Nulled()
    {
        var v = SafetyPolicy.Validate(TuneSettings.Factory with { VoltageOffsetMv = -50 }, TestGpus.Caps(voltage: false), Normal);
        Assert.Null(v.Clamped.VoltageOffsetMv);
        Assert.Equal([SafetyIssue.Unsupported], v.Issues);
    }

    [Fact]
    public void Validate_Factory_Ok()
    {
        var v = SafetyPolicy.Validate(TuneSettings.Factory, TestGpus.Caps(), Normal);
        Assert.True(v.Ok);
        Assert.True(v.Clamped.IsFactory);
    }
}
