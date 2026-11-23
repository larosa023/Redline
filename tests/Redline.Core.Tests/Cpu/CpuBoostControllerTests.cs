using Redline.Core.Cpu;
using Redline.Core.Tests.Fakes;
using Xunit;

namespace Redline.Core.Tests.Cpu;

public class CpuBoostControllerTests : IDisposable
{
    readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    sealed class FakePlan : IPowerPlan
    {
        public CpuBoostSettings Current = new(CpuBoostMode.Aggressive, 100);
        public CpuBoostSettings ReadCurrent() => Current;
        public void Apply(CpuBoostSettings s) => Current = new(s.BoostMode ?? Current.BoostMode, s.MaxProcessorStatePct ?? Current.MaxProcessorStatePct);
    }

    string Orig => _dir.File("cpu-original.json");

    [Fact]
    public void FirstApply_SavesOriginal()
    {
        var plan = new FakePlan();
        new CpuBoostController(plan, Orig).Apply(new(CpuBoostMode.Disabled, null));
        Assert.True(File.Exists(Orig));
        Assert.Equal(CpuBoostMode.Disabled, plan.Current.BoostMode);
    }

    [Fact]
    public void Restore_PutsOriginalBack()
    {
        var plan = new FakePlan();
        var c = new CpuBoostController(plan, Orig);
        c.Apply(new(CpuBoostMode.Disabled, 80));
        c.Apply(new(CpuBoostMode.EfficientEnabled, null));   // second apply must not overwrite the saved original
        c.RestoreOriginal();
        Assert.Equal(new CpuBoostSettings(CpuBoostMode.Aggressive, 100), plan.Current);
        Assert.False(File.Exists(Orig));
    }

    [Fact]
    public void Original_SurvivesRestart()
    {
        var plan = new FakePlan();
        new CpuBoostController(plan, Orig).Apply(new(CpuBoostMode.Disabled, null));
        new CpuBoostController(plan, Orig).RestoreOriginal();
        Assert.Equal(CpuBoostMode.Aggressive, plan.Current.BoostMode);
    }

    [Fact]
    public void Null_Restores()
    {
        var plan = new FakePlan();
        var c = new CpuBoostController(plan, Orig);
        c.Apply(new(CpuBoostMode.Disabled, null));
        c.Apply(null);
        Assert.Equal(CpuBoostMode.Aggressive, plan.Current.BoostMode);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(101)]
    public void MaxState_OutOfRange_Throws(int pct) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CpuBoostController(new FakePlan(), Orig).Apply(new(null, pct)));

    [Fact]
    public void Restore_WithNothingSaved_DoesNothing()
    {
        var plan = new FakePlan();
        new CpuBoostController(plan, Orig).RestoreOriginal();
        Assert.Equal(CpuBoostMode.Aggressive, plan.Current.BoostMode);
    }
}
