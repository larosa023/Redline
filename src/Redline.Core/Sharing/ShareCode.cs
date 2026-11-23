using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Redline.Core.Cpu;
using Redline.Core.Gpu;
using Redline.Core.Storage;

namespace Redline.Core.Sharing;

public enum ShareCodeError { Malformed, ChecksumMismatch, UnsupportedVersion }

public sealed record ShareCodeResult(Profile? Profile, string? GpuModel, ShareCodeError? Error, bool ModelMismatch);

/// <summary>Copy/paste codes for sharing a tune: RL1.&lt;base64url(deflate(json))&gt;.&lt;6 hex checksum&gt;.</summary>
public static class ShareCode
{
    const string Prefix = "RL1";
    // Positional, short payload: [v, model, name, icon, power, core, mem, volt, [[t,p]..]|null, boostMode, maxState]
    sealed record Payload(int V, string Model, string Name, string Icon, TuneSettings Gpu, CpuBoostSettings? Cpu)
    {
        public object?[] ToArray() =>
        [
            V, Model, Name, Icon, Gpu.PowerLimitPct, Gpu.CoreOffsetMhz, Gpu.MemoryOffsetMhz, Gpu.VoltageOffsetMv,
            Gpu.Fan?.Points.Select(x => new[] { x.TempC, x.Pct }).ToArray(), (int?)Cpu?.BoostMode, Cpu?.MaxProcessorStatePct,
        ];

        public static Payload FromArray(JsonElement a)
        {
            static int? I(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetInt32();
            var fan = a[8].ValueKind == JsonValueKind.Null ? null
                : new Fans.FanCurve(a[8].EnumerateArray().Select(x => new Fans.FanPoint(x[0].GetInt32(), x[1].GetInt32())).ToList());
            var boost = I(a[9]);
            var max = I(a[10]);
            var cpu = boost is null && max is null ? null : new CpuBoostSettings(boost is { } b ? (CpuBoostMode)b : null, max);
            return new Payload(a[0].GetInt32(), a[1].GetString()!, a[2].GetString()!, a[3].GetString()!,
                new TuneSettings(I(a[4]), I(a[5]), I(a[6]), I(a[7]), fan), cpu);
        }
    }

    static readonly JsonSerializerOptions Compact = new(Json.Options) { WriteIndented = false };

    public static string Encode(Profile p, string gpuModel)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new Payload(1, gpuModel, p.Name, p.Icon, p.Gpu, p.Cpu).ToArray(), Compact);
        using var ms = new MemoryStream();
        using (var z = new DeflateStream(ms, CompressionLevel.SmallestSize, leaveOpen: true)) z.Write(json);
        var b64 = Convert.ToBase64String(ms.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Prefix}.{b64}.{Checksum(b64)}";
    }

    public static ShareCodeResult Decode(string code, string localGpuModel)
    {
        var parts = (code ?? "").Trim().Split('.');
        if (parts.Length != 3 || !parts[0].StartsWith("RL", StringComparison.Ordinal)) return Fail(ShareCodeError.Malformed);
        if (parts[0] != Prefix) return Fail(ShareCodeError.UnsupportedVersion);
        if (!string.Equals(Checksum(parts[1]), parts[2], StringComparison.OrdinalIgnoreCase)) return Fail(ShareCodeError.ChecksumMismatch);
        try
        {
            var b64 = parts[1].Replace('-', '+').Replace('_', '/');
            b64 += new string('=', (4 - b64.Length % 4) % 4);
            using var z = new DeflateStream(new MemoryStream(Convert.FromBase64String(b64)), CompressionMode.Decompress);
            using var doc = JsonDocument.Parse(z);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() != 11) return Fail(ShareCodeError.Malformed);
            var p = Payload.FromArray(doc.RootElement);
            if (p.V != 1) return Fail(ShareCodeError.UnsupportedVersion);
            var profile = new Profile("imported-" + Guid.NewGuid().ToString("N")[..8], p.Name, p.Icon, p.Gpu, p.Cpu, false, null, null);
            return new ShareCodeResult(profile, p.Model, null, !string.Equals(p.Model, localGpuModel, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception e) when (e is FormatException or JsonException or InvalidDataException or InvalidOperationException or IndexOutOfRangeException)
        {
            return Fail(ShareCodeError.Malformed);
        }
    }

    static ShareCodeResult Fail(ShareCodeError e) => new(null, null, e, false);

    static string Checksum(string payload) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(payload)))[..6].ToLowerInvariant();
}
