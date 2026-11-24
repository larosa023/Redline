using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Redline.Core.AutoTune;
using Redline.Core.Gpu;
using Redline.Core.Stress;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Redline.Stress;

/// <summary>
/// GPU stress test: a deterministic integer compute shader fills a large buffer (core load), then re-reads and
/// verifies it (memory load). Any checksum or verify mismatch is a hardware error. Device removal = driver reset.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class D3D11StressRunner(GoldenStore golden, uint? elements = null, uint iterations = 32, bool forceWarp = false) : IStressRunner
{
    const uint GroupSize = 256, GroupsX = 4096;
    static readonly uint[] Seeds = [7919u, 15838u, 23757u, 31676u];

    [StructLayout(LayoutKind.Sequential)]
    struct Params { public uint Seed, Iter, N, RowWidth; }

    public Task<StressResult> RunAsync(IGpuDevice d, TimeSpan duration, CancellationToken ct) =>
        Task.Factory.StartNew(() => Run(d, duration, ct), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    StressResult Run(IGpuDevice d, TimeSpan duration, CancellationToken ct)
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        using var adapter = forceWarp || d.Vendor == Vendor.Simulated ? null : PickAdapter(factory, d.Name);
        var levels = new[] { FeatureLevel.Level_11_0 };
        var hr = adapter is null
            ? D3D11.D3D11CreateDevice(null, DriverType.Warp, DeviceCreationFlags.None, levels, out var device)
            : D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.None, levels, out device);
        if (hr.Failure || device is null) return new StressResult(false, 0, 0, false, 0, 0);
        using var _ = device;
        var ctx = device.ImmediateContext;

        var n = elements ?? ChooseElements(adapter);
        var source = LoadShader();
        using var csCompute = device.CreateComputeShader(Compiler.Compile(source, "Compute", "stress.hlsl", "cs_5_0").Span);
        using var csVerify = device.CreateComputeShader(Compiler.Compile(source, "Verify", "stress.hlsl", "cs_5_0").Span);
        using var csReset = device.CreateComputeShader(Compiler.Compile(source, "Reset", "stress.hlsl", "cs_5_0").Span);
        using var csStreamInit = device.CreateComputeShader(Compiler.Compile(source, "StreamInit", "stress.hlsl", "cs_5_0").Span);
        using var csStream = device.CreateComputeShader(Compiler.Compile(source, "Stream", "stress.hlsl", "cs_5_0").Span);
        using var csStreamVerify = device.CreateComputeShader(Compiler.Compile(source, "StreamVerify", "stress.hlsl", "cs_5_0").Span);

        using var data = device.CreateBuffer(new BufferDescription(n * 4, BindFlags.UnorderedAccess, ResourceUsage.Default,
            CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 4));
        using var result = device.CreateBuffer(new BufferDescription(8, BindFlags.UnorderedAccess, ResourceUsage.Default,
            CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 4));
        using var staging = device.CreateBuffer(new BufferDescription(8, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
        using var cb = device.CreateBuffer(new BufferDescription(16, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        using var dataUav = device.CreateUnorderedAccessView(data, new UnorderedAccessViewDescription(data, Format.Unknown, 0u, n));
        using var resUav = device.CreateUnorderedAccessView(result, new UnorderedAccessViewDescription(result, Format.Unknown, 0u, 2u));

        // Bandwidth phase: two buffers of uint4, each the same size as the main buffer (n * 4 bytes).
        var sn = Math.Max(1u, n / 4);
        using var src = device.CreateBuffer(new BufferDescription(sn * 16, BindFlags.UnorderedAccess, ResourceUsage.Default,
            CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 16));
        using var dst = device.CreateBuffer(new BufferDescription(sn * 16, BindFlags.UnorderedAccess, ResourceUsage.Default,
            CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 16));
        using var srcUav = device.CreateUnorderedAccessView(src, new UnorderedAccessViewDescription(src, Format.Unknown, 0u, sn));
        using var dstUav = device.CreateUnorderedAccessView(dst, new UnorderedAccessViewDescription(dst, Format.Unknown, 0u, sn));
        const int StreamPasses = 8;
        double streamBytes = 0, streamSecs = 0;

        var rowWidth = GroupsX * GroupSize;
        var groupsY = (n + rowWidth - 1) / rowWidth;
        var groupsX = n < rowWidth ? (n + GroupSize - 1) / GroupSize : GroupsX;
        var expected = Seeds.Select(s => golden.Get(s, iterations, n)).ToArray();
        var sGroupsY = (sn + rowWidth - 1) / rowWidth;
        var sGroupsX = sn < rowWidth ? (sn + GroupSize - 1) / GroupSize : GroupsX;

        long errors = 0, rounds = 0;
        double powerSum = 0, maxTemp = 0; var samples = 0;
        var sw = Stopwatch.StartNew();
        var nextSample = TimeSpan.Zero;
        try
        {
            ctx.CSSetUnorderedAccessView(0, dataUav);
            ctx.CSSetUnorderedAccessView(1, resUav);
            ctx.CSSetUnorderedAccessView(2, srcUav);
            ctx.CSSetUnorderedAccessView(3, dstUav);
            ctx.CSSetConstantBuffer(0, cb);
            SetParams(ctx, cb, new Params { Seed = 0, Iter = iterations, N = sn, RowWidth = rowWidth });
            ctx.CSSetShader(csStreamInit); ctx.Dispatch(sGroupsX, sGroupsY, 1);
            while (sw.Elapsed < duration && !ct.IsCancellationRequested)
            {
                var seed = Seeds[rounds % Seeds.Length];
                var p = new Params { Seed = seed, Iter = iterations, N = n, RowWidth = rowWidth };
                var m = ctx.Map(cb, 0, MapMode.WriteDiscard);
                Marshal.StructureToPtr(p, m.DataPointer, false);
                ctx.Unmap(cb, 0);

                ctx.CSSetShader(csReset); ctx.Dispatch(1, 1, 1);
                ctx.CSSetShader(csCompute); ctx.Dispatch(groupsX, groupsY, 1);
                ctx.CSSetShader(csVerify); ctx.Dispatch(groupsX, groupsY, 1);
                ctx.CopyResource(staging, result);
                var r = ctx.Map(staging, 0, MapMode.Read);
                var checksum = (uint)Marshal.ReadInt32(r.DataPointer);
                var verifyErrors = (uint)Marshal.ReadInt32(r.DataPointer, 4);
                ctx.Unmap(staging, 0);

                if (checksum != expected[rounds % Seeds.Length]) errors++;
                errors += verifyErrors;

                // Bandwidth: the GPU is idle here (we just waited on the result), so wall time ≈ GPU time.
                SetParams(ctx, cb, new Params { Seed = seed, Iter = iterations, N = sn, RowWidth = rowWidth });
                var t0 = sw.Elapsed;
                ctx.CSSetShader(csReset); ctx.Dispatch(1, 1, 1);
                ctx.CSSetShader(csStream);
                for (var k = 0; k < StreamPasses; k++) ctx.Dispatch(sGroupsX, sGroupsY, 1);
                ctx.CSSetShader(csStreamVerify); ctx.Dispatch(sGroupsX, sGroupsY, 1);
                ctx.CopyResource(staging, result);
                var sr = ctx.Map(staging, 0, MapMode.Read);
                errors += (uint)Marshal.ReadInt32(sr.DataPointer, 4);
                ctx.Unmap(staging, 0);
                streamSecs += (sw.Elapsed - t0).TotalSeconds;
                streamBytes += (StreamPasses * 2 + 1) * (double)sn * 16;
                rounds++;

                if (sw.Elapsed >= nextSample)
                {
                    nextSample = sw.Elapsed + TimeSpan.FromSeconds(1);
                    if (d.ReadTelemetry() is { } t) { powerSum += t.PowerW; maxTemp = Math.Max(maxTemp, t.TempC); samples++; }
                }
            }
        }
        catch (SharpGenException e) when (IsDeviceLost(e.ResultCode) || device.DeviceRemovedReason.Failure)
        {
            return new StressResult(false, errors, 0, true, samples > 0 ? powerSum / samples : 0, maxTemp);
        }
        var secs = Math.Max(0.001, sw.Elapsed.TotalSeconds);
        return new StressResult(!ct.IsCancellationRequested, errors, rounds * (double)n / 1e6 / Math.Max(0.001, secs - streamSecs), false,
            samples > 0 ? powerSum / samples : 0, maxTemp)
        {
            MemBandwidthGBs = streamSecs > 0 ? streamBytes / streamSecs / 1e9 : null,
        };
    }

    static void SetParams(ID3D11DeviceContext ctx, ID3D11Buffer cb, Params p)
    {
        var m = ctx.Map(cb, 0, MapMode.WriteDiscard);
        Marshal.StructureToPtr(p, m.DataPointer, false);
        ctx.Unmap(cb, 0);
    }

    static bool IsDeviceLost(Result r) =>
        r.Code == unchecked((int)0x887A0005) || r.Code == unchecked((int)0x887A0007) || r.Code == unchecked((int)0x887A0006);

    static IDXGIAdapter1? PickAdapter(IDXGIFactory1 f, string name)
    {
        var all = new List<IDXGIAdapter1>();
        for (uint i = 0; f.EnumAdapters1(i, out var a).Success; i++) all.Add(a);
        var best = all.FirstOrDefault(a => name.Contains(a.Description1.Description.Trim(), StringComparison.OrdinalIgnoreCase)
                                           || a.Description1.Description.Contains(name, StringComparison.OrdinalIgnoreCase))
                   ?? all.Where(a => (a.Description1.Flags & AdapterFlags.Software) == 0).OrderByDescending(a => a.Description1.DedicatedVideoMemory).FirstOrDefault();
        foreach (var a in all) if (a != best) a.Dispose();
        return best;
    }

    static uint ChooseElements(IDXGIAdapter1? a)
    {
        const uint max = 64u * 1024 * 1024;                 // 256 MB of uints
        if (a is null) return 1u << 20;
        var vram = (ulong)a.Description1.DedicatedVideoMemory;
        return (uint)Math.Clamp(vram / 2 / 4 / 4, 1u << 20, max);   // ≤ 1/8 of VRAM
    }

    static string LoadShader()
    {
        using var s = typeof(D3D11StressRunner).Assembly.GetManifestResourceStream("Redline.Stress.stress.hlsl")!;
        return new StreamReader(s).ReadToEnd();
    }
}
