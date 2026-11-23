using System.Collections.Concurrent;
using System.Text.Json;
using Redline.Core.AutoTune;
using Redline.Core.Storage;

namespace Redline.Core.Stress;

/// <summary>
/// The stress-test math, shared by the GPU shader (stress.hlsl must match exactly) and the CPU reference.
/// Integer-only, so the expected result is exact and any difference means the hardware made an error.
/// </summary>
public static class StressKernel
{
    public static uint Element(uint i, uint seed, uint iter)
    {
        uint x = unchecked(i * 2654435761u) ^ seed;
        for (uint k = 0; k < iter; k++)
        {
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            x = unchecked(x * 1664525u + 1013904223u);
        }
        return x;
    }

    public static uint Checksum(uint seed, uint iter, uint n)
    {
        long total = 0;
        var chunks = Math.Max(1, Environment.ProcessorCount * 4);
        var size = (n + (uint)chunks - 1) / (uint)chunks;
        Parallel.For(0, chunks, c =>
        {
            uint local = 0;
            var start = (uint)c * size;
            var end = Math.Min(n, start + size);
            for (var i = start; i < end; i++) local = unchecked(local + Element(i, seed, iter));
            Interlocked.Add(ref total, local);
        });
        return unchecked((uint)total);
    }
}

/// <summary>Caches expected checksums on disk (they depend only on seed, iterations and size).</summary>
public sealed class GoldenStore(string dir)
{
    readonly string _path = Path.Combine(dir, "stress-golden.json");
    readonly ConcurrentDictionary<string, uint> _cache = Load(Path.Combine(dir, "stress-golden.json"));

    static ConcurrentDictionary<string, uint> Load(string p)
    {
        try { return new(JsonSerializer.Deserialize<Dictionary<string, uint>>(File.ReadAllText(p), Json.Options) ?? new()); }
        catch (Exception e) when (e is IOException or JsonException) { return new(); }
    }

    public uint Get(uint seed, uint iter, uint n)
    {
        var key = $"{seed}:{iter}:{n}";
        if (_cache.TryGetValue(key, out var v)) return v;
        v = StressKernel.Checksum(seed, iter, n);
        _cache[key] = v;
        try { Json.WriteAtomic(_path, JsonSerializer.Serialize(_cache.ToDictionary(k => k.Key, k => k.Value), Json.Options)); }
        catch (IOException) { }
        return v;
    }
}

/// <summary>CPU stress: one worker per logical core running the kernel; each round's checksum must match the reference.</summary>
public sealed class CpuStressRunner(int workPerThread = 2_000_000)
{
    const uint Iter = 16;
    public Func<int, int, bool>? FaultInjector { get; init; }

    public async Task<StressResult> RunAsync(TimeSpan duration, CancellationToken ct)
    {
        var threads = Environment.ProcessorCount;
        var seeds = Enumerable.Range(1, 4).Select(s => (uint)s * 7919u).ToArray();
        var expected = await Task.Factory.StartNew(() => seeds.Select(s => StressKernel.Checksum(s, Iter, (uint)workPerThread)).ToArray(),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        long errors = 0, rounds = 0;
        var deadline = DateTime.UtcNow + duration;
        // Dedicated threads (not the thread pool) so timers and cancellation stay responsive under full load.
        var workers = Enumerable.Range(0, threads).Select(t => Task.Factory.StartNew(() =>
        {
            for (var round = 0; DateTime.UtcNow < deadline; round++)
            {
                if (ct.IsCancellationRequested) return;
                var si = (t + round) % seeds.Length;
                uint sum = 0;
                for (uint i = 0; i < workPerThread; i++)
                {
                    sum = unchecked(sum + StressKernel.Element(i, seeds[si], Iter));
                    if ((i & 0x3FFF) == 0 && ct.IsCancellationRequested) return;
                }
                if (FaultInjector?.Invoke(t, round) == true) sum ^= 1;
                if (sum != expected[si]) Interlocked.Increment(ref errors);
                Interlocked.Increment(ref rounds);
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        await Task.WhenAll(workers);
        return new StressResult(!ct.IsCancellationRequested, errors, rounds / Math.Max(0.001, duration.TotalSeconds), false, 0, 0);
    }
}
