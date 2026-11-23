using System.Text.Json;
using Redline.Core.Gpu;
using Redline.Core.Storage;

namespace Redline.Core.Score;

public sealed record ModelReference(string ModelPattern, int TypicalCoreOffsetMhz, int TypicalMemOffsetMhz);

public static class ReferenceData
{
    static readonly Lazy<IReadOnlyList<ModelReference>> _all = new(() =>
    {
        using var s = typeof(ReferenceData).Assembly.GetManifestResourceStream("Redline.Core.Score.reference.json")
            ?? throw new InvalidOperationException("reference.json missing");
        return JsonSerializer.Deserialize<List<ModelReference>>(s, Json.Options)!;
    });

    public static IReadOnlyList<ModelReference> All => _all.Value;

    /// <summary>Longest pattern contained in the GPU name wins ("RTX 4070 Ti SUPER" over "RTX 4070").</summary>
    public static ModelReference? Find(string gpuName) => All
        .Where(r => gpuName.Contains(r.ModelPattern, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(r => r.ModelPattern.Length)
        .FirstOrDefault();
}

public sealed record ChipScoreResult(int Score, string Tier, string ModelMatched);

public static class ChipScore
{
    public static ChipScoreResult? Compute(string gpuName, TuneSettings stableTune)
    {
        if (ReferenceData.Find(gpuName) is not { } r) return null;
        var core = (stableTune.CoreOffsetMhz ?? 0) / (double)Math.Max(1, r.TypicalCoreOffsetMhz);
        var mem = (stableTune.MemoryOffsetMhz ?? 0) / (double)Math.Max(1, r.TypicalMemOffsetMhz);
        var score = (int)Math.Clamp(Math.Round(50 + 50 * ((core + mem) / 2 - 1), MidpointRounding.AwayFromZero), 0, 100);
        var tier = score switch { < 35 => "Solid", < 65 => "Good chip", < 85 => "Great chip", _ => "Golden" };
        return new ChipScoreResult(score, tier, r.ModelPattern);
    }
}
