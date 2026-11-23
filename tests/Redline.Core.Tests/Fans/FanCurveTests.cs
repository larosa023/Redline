using Redline.Core.Fans;
using Xunit;

namespace Redline.Core.Tests.Fans;

public class FanCurveTests
{
    static FanCurve C(params (int t, int p)[] pts) => new(pts.Select(x => new FanPoint(x.t, x.p)).ToList());

    [Fact]
    public void Evaluate_Interpolates() => Assert.Equal(48, FanCurve.Default.Evaluate(57.5));

    [Fact]
    public void Evaluate_BelowFirst_FirstPct() => Assert.Equal(30, FanCurve.Default.Evaluate(10));

    [Fact]
    public void Evaluate_AboveLast_LastPct() => Assert.Equal(100, FanCurve.Default.Evaluate(97));

    [Fact]
    public void Evaluate_ExactPoint() => Assert.Equal(75, FanCurve.Default.Evaluate(75));

    [Fact]
    public void Validate_DecreasingPct_Error() =>
        Assert.NotEmpty(C((30, 50), (60, 40), (85, 90)).Validate(minPct: 30));

    [Fact]
    public void Validate_TempsNotIncreasing_Error() =>
        Assert.NotEmpty(C((30, 30), (30, 40), (85, 90)).Validate(minPct: 30));

    [Fact]
    public void Validate_HotPointBelow80_Error() =>
        Assert.NotEmpty(C((30, 30), (60, 50), (90, 70)).Validate(minPct: 30));

    [Fact]
    public void Validate_BelowDriverMin_Error() =>
        Assert.NotEmpty(C((30, 20), (60, 50), (85, 90)).Validate(minPct: 30));

    [Fact]
    public void Validate_TooFewOrTooManyPoints_Error()
    {
        Assert.NotEmpty(C((30, 30)).Validate(30));
        Assert.NotEmpty(C(Enumerable.Range(0, 9).Select(i => (20 + i * 9, 30 + i * 8)).ToArray()).Validate(30));
    }

    [Fact]
    public void Validate_TempOutOfRange_Error() =>
        Assert.NotEmpty(C((10, 30), (60, 50), (85, 90)).Validate(30));

    [Fact]
    public void Default_IsValid() => Assert.Empty(FanCurve.Default.Validate(minPct: 30));
}
