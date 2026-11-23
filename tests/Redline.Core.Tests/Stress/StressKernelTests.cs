using Redline.Core.Stress;
using Redline.Core.Tests.Fakes;
using Xunit;

namespace Redline.Core.Tests.Stress;

public class StressKernelTests
{
    [Fact]
    public void Element_IsDeterministic() => Assert.Equal(StressKernel.Element(12345, 7, 32), StressKernel.Element(12345, 7, 32));

    [Fact]
    public void Element_KnownValue()
    {
        // x = i*2654435761 ^ seed, then iter rounds of xorshift + LCG (all uint32 wrap-around)
        uint x = unchecked(3u * 2654435761u) ^ 99u;
        for (var k = 0; k < 2; k++) { x ^= x << 13; x ^= x >> 17; x ^= x << 5; x = unchecked(x * 1664525u + 1013904223u); }
        Assert.Equal(x, StressKernel.Element(3, 99, 2));
    }

    [Fact]
    public void Checksum_SumsElements()
    {
        uint sum = 0;
        for (uint i = 0; i < 1000; i++) sum = unchecked(sum + StressKernel.Element(i, 5, 8));
        Assert.Equal(sum, StressKernel.Checksum(5, 8, 1000));
    }

    [Fact]
    public void GoldenStore_CachesAcrossInstances()
    {
        using var d = new TempDir();
        var a = new GoldenStore(d.Path).Get(5, 8, 1000);
        Assert.Equal(StressKernel.Checksum(5, 8, 1000), a);
        Assert.True(File.Exists(Path.Combine(d.Path, "stress-golden.json")));
        Assert.Equal(a, new GoldenStore(d.Path).Get(5, 8, 1000));
    }

    [Fact]
    public async Task CpuStress_ChecksumsDeterministic_NoErrors()
    {
        var r = new CpuStressRunner(workPerThread: 20_000);
        var res = await r.RunAsync(TimeSpan.FromMilliseconds(300), default);
        Assert.True(res.Completed);
        Assert.Equal(0, res.Errors);
        Assert.True(res.Score > 0);
    }

    [Fact]
    public async Task CpuStress_InjectedFault_CountsErrors()
    {
        var r = new CpuStressRunner(workPerThread: 20_000) { FaultInjector = (thread, round) => thread == 0 && round == 1 };
        var res = await r.RunAsync(TimeSpan.FromMilliseconds(300), default);
        Assert.True(res.Errors > 0);
    }

    [Fact]
    public async Task CpuStress_Cancellation_StopsWithin1s()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var res = await new CpuStressRunner(workPerThread: 20_000).RunAsync(TimeSpan.FromMinutes(5), cts.Token);
        Assert.False(res.Completed);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), $"took {sw.Elapsed}");
    }
}
