using Redline.Core.Gpu;
using Redline.Core.Safety;
using Redline.Core.Sim;
using Redline.Core.Tests.Fakes;
using Redline.Engine;
using Xunit;

namespace Redline.Engine.Tests;

public class BeforeAfterRunnerTests
{
    [Fact]
    public async Task WarmsUp_ThenAlternatesFactoryAndTune()
    {
        using var dir = new TempDir();
        var gpu = new SimGpuDevice(TestGpus.Spec());
        var stress = new SimStressRunner();
        var tuned = new TuneSettings(null, 150, 500, null, null);
        var runner = new BeforeAfterRunner(gpu, stress, new SafeStartJournal(dir.File("journal.json")));

        var r = await runner.RunAsync(tuned, tuned, default);

        var seen = stress.Tested.Select(s => s.IsFactory ? "F" : "T").ToArray();
        Assert.Equal(["F", "F", "T", "F", "T"], seen);   // warm-up (not counted), then F T F T
        Assert.True(r.TunedScore > r.StockScore);
        Assert.Equal(tuned, gpu.Current);                 // put back afterwards
    }
}
