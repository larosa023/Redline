using Redline.Core.Gpu;
using Redline.Core.Score;
using Xunit;

namespace Redline.Core.Tests.Score;

public class ChipScoreTests
{
    static ModelReference Ref4090 => ReferenceData.Find("NVIDIA GeForce RTX 4090")!;

    [Fact]
    public void ReferenceJson_LoadsAndHasRtx4090()
    {
        Assert.NotNull(ReferenceData.Find("NVIDIA GeForce RTX 4090"));
        Assert.True(ReferenceData.All.Count >= 20);
    }

    [Fact]
    public void Find_PrefersMostSpecificPattern()
    {
        Assert.Equal("RTX 4070 Ti SUPER", ReferenceData.Find("NVIDIA GeForce RTX 4070 Ti SUPER")!.ModelPattern);
        Assert.Equal("RTX 4070", ReferenceData.Find("NVIDIA GeForce RTX 4070")!.ModelPattern);
    }

    [Fact]
    public void TypicalOffsets_Score50_Good()
    {
        var r = ChipScore.Compute("NVIDIA GeForce RTX 4090",
            TuneSettings.Factory with { CoreOffsetMhz = Ref4090.TypicalCoreOffsetMhz, MemoryOffsetMhz = Ref4090.TypicalMemOffsetMhz })!;
        Assert.Equal(50, r.Score);
        Assert.Equal("Good chip", r.Tier);
    }

    [Fact]
    public void DoubleTypical_Score100_Golden()
    {
        var r = ChipScore.Compute("NVIDIA GeForce RTX 4090",
            TuneSettings.Factory with { CoreOffsetMhz = Ref4090.TypicalCoreOffsetMhz * 2, MemoryOffsetMhz = Ref4090.TypicalMemOffsetMhz * 2 })!;
        Assert.Equal(100, r.Score);
        Assert.Equal("Golden", r.Tier);
    }

    [Fact]
    public void Zero_Score0_Solid()
    {
        var r = ChipScore.Compute("NVIDIA GeForce RTX 4090", TuneSettings.Factory)!;
        Assert.Equal(0, r.Score);
        Assert.Equal("Solid", r.Tier);
    }

    [Fact]
    public void UnknownModel_Null() => Assert.Null(ChipScore.Compute("Mystery GPU 9000", TuneSettings.Factory));
}
