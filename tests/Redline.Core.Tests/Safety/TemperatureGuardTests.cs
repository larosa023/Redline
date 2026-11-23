using Redline.Core.Gpu;
using Redline.Core.Safety;
using Xunit;

namespace Redline.Core.Tests.Safety;

public class TemperatureGuardTests
{
    static readonly TuneSettings Tuned = new(120, 150, 1000, null, null);
    static Telemetry T(double temp) => new(DateTimeOffset.UnixEpoch, 2800, 10500, temp, null, 400, 99, 60, 1500, 8000);

    [Fact]
    public void BelowCeiling_None()
    {
        var g = new TemperatureGuard();
        for (var i = 0; i < 10; i++) Assert.IsType<GuardAction.None>(g.Evaluate(T(82), Tuned));
    }

    [Fact]
    public void DefaultCeiling_Is83() => Assert.Equal(83, new TemperatureGuard().CeilingC);

    [Fact]
    public void FiveSamplesAtCeiling_ScaleBackHalvesOffsets()
    {
        var g = new TemperatureGuard();
        for (var i = 0; i < 4; i++) Assert.IsType<GuardAction.None>(g.Evaluate(T(83), Tuned));
        var a = Assert.IsType<GuardAction.ScaleBack>(g.Evaluate(T(84), Tuned));
        Assert.Equal(75, a.Reduced.CoreOffsetMhz);
        Assert.Equal(500, a.Reduced.MemoryOffsetMhz);
        Assert.Equal(100, a.Reduced.PowerLimitPct);
    }

    [Fact]
    public void FourSamples_ThenCool_None()
    {
        var g = new TemperatureGuard();
        for (var i = 0; i < 4; i++) g.Evaluate(T(84), Tuned);
        Assert.IsType<GuardAction.None>(g.Evaluate(T(70), Tuned));
        for (var i = 0; i < 4; i++) Assert.IsType<GuardAction.None>(g.Evaluate(T(84), Tuned));
    }

    [Fact]
    public void CeilingPlus5_RevertImmediately() =>
        Assert.IsType<GuardAction.RevertToFactory>(new TemperatureGuard().Evaluate(T(88), Tuned));

    [Fact]
    public void Factory_NeverActs()
    {
        var g = new TemperatureGuard();
        for (var i = 0; i < 10; i++) Assert.IsType<GuardAction.None>(g.Evaluate(T(95), TuneSettings.Factory));
        for (var i = 0; i < 10; i++) Assert.IsType<GuardAction.None>(g.Evaluate(null, TuneSettings.Factory));
    }

    [Theory]
    [InlineData(69)]
    [InlineData(91)]
    public void Ceiling_OutOfRange_Throws(int c) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new TemperatureGuard { CeilingC = c });

    [Fact]
    public void NullTelemetry5x_WhileTuned_Revert()
    {
        var g = new TemperatureGuard();
        for (var i = 0; i < 4; i++) Assert.IsType<GuardAction.None>(g.Evaluate(null, Tuned));
        var a = Assert.IsType<GuardAction.RevertToFactory>(g.Evaluate(null, Tuned));
        Assert.Contains("lost", a.Reason);
    }
}
