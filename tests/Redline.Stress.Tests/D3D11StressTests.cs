using Redline.Core.Sim;
using Redline.Core.Stress;
using Redline.Core.Tests.Fakes;
using Redline.Stress;
using Xunit;

namespace Redline.Stress.Tests;

public class D3D11StressTests
{
    static SimGpuDevice Sim => new(TestGpus.Spec());

    [Fact]
    public async Task Warp_RunsAndScoresPositive_NoErrors()
    {
        using var d = new TempDir();
        var r = await new D3D11StressRunner(new GoldenStore(d.Path), elements: 1u << 18, iterations: 8, forceWarp: true)
            .RunAsync(Sim, TimeSpan.FromSeconds(2), default);
        Assert.True(r.Completed);
        Assert.Equal(0, r.Errors);
        Assert.True(r.Score > 0);
        Assert.False(r.DriverReset);
    }

    [Fact]
    public async Task WrongExpectedChecksum_ReportsErrors()
    {
        using var d = new TempDir();
        // Poison the golden cache so the GPU's (correct) result no longer matches.
        File.WriteAllText(Path.Combine(d.Path, "stress-golden.json"),
            """{ "7919:8:262144": 1, "15838:8:262144": 1, "23757:8:262144": 1, "31676:8:262144": 1 }""");
        var r = await new D3D11StressRunner(new GoldenStore(d.Path), elements: 1u << 18, iterations: 8, forceWarp: true)
            .RunAsync(Sim, TimeSpan.FromSeconds(1), default);
        Assert.True(r.Errors > 0);
    }

    [Fact]
    public async Task Cancellation_StopsWithin1s()
    {
        using var d = new TempDir();
        var runner = new D3D11StressRunner(new GoldenStore(d.Path), elements: 1u << 18, iterations: 8, forceWarp: true);
        await runner.RunAsync(Sim, TimeSpan.FromMilliseconds(200), default);   // warm up (shader compile, golden)
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = await runner.RunAsync(Sim, TimeSpan.FromMinutes(5), cts.Token);
        Assert.False(r.Completed);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1.5), $"took {sw.Elapsed}");
    }
}
