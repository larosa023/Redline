using Redline.Core.Gpu;
using Redline.Core.Safety;
using Redline.Core.Tests.Fakes;
using Xunit;

namespace Redline.Core.Tests.Safety;

public class KeepOrRevertTests
{
    static readonly TuneSettings Applied = TuneSettings.Factory with { CoreOffsetMhz = 150 };

    [Fact]
    public void Default_Timeout_Is15Seconds()
    {
        var t = new FakeTime();
        var s = new KeepOrRevertSession(TuneSettings.Factory, Applied, t);
        Assert.Equal(TimeSpan.FromSeconds(15), s.Remaining);
        t.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(10), s.Remaining);
    }

    [Fact]
    public async Task Confirm_BeforeTimeout_Kept()
    {
        var t = new FakeTime();
        var s = new KeepOrRevertSession(TuneSettings.Factory, Applied, t);
        t.Advance(TimeSpan.FromSeconds(14));
        s.Confirm();
        Assert.Equal(KeepOrRevertOutcome.Kept, await s.Completion);
        t.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(KeepOrRevertOutcome.Kept, await s.Completion);
    }

    [Fact]
    public async Task NoConfirm_After15s_Reverted()
    {
        var t = new FakeTime();
        var s = new KeepOrRevertSession(TuneSettings.Factory, Applied, t);
        t.Advance(TimeSpan.FromSeconds(14.9));
        Assert.False(s.Completion.IsCompleted);
        t.Advance(TimeSpan.FromSeconds(0.2));
        Assert.Equal(KeepOrRevertOutcome.Reverted, await s.Completion);
        Assert.Equal(TimeSpan.Zero, s.Remaining);
    }

    [Fact]
    public async Task Confirm_AfterTimeout_StaysReverted()
    {
        var t = new FakeTime();
        var s = new KeepOrRevertSession(TuneSettings.Factory, Applied, t);
        t.Advance(TimeSpan.FromSeconds(16));
        s.Confirm();
        Assert.Equal(KeepOrRevertOutcome.Reverted, await s.Completion);
    }

    [Fact]
    public void Tokens_AreUnique()
    {
        var t = new FakeTime();
        Assert.NotEqual(new KeepOrRevertSession(TuneSettings.Factory, Applied, t).Token,
                        new KeepOrRevertSession(TuneSettings.Factory, Applied, t).Token);
    }
}
