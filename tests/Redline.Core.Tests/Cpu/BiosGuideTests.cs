using Redline.Core.Cpu;
using Xunit;

namespace Redline.Core.Tests.Cpu;

public class BiosGuideTests
{
    [Fact]
    public void Ryzen7900X_GetsRyzen7000Guide() =>
        Assert.Contains("Ryzen 7000", BiosGuides.For("AMD Ryzen 9 7900X 12-Core Processor").Title);

    [Fact]
    public void Ryzen9800X3D_GetsX3DGuide() =>
        Assert.Contains("X3D", BiosGuides.For("AMD Ryzen 7 9800X3D 8-Core Processor").Title);

    [Fact]
    public void Intel14900K_GetsIntelGuide() =>
        Assert.Contains("Intel", BiosGuides.For("13th Gen Intel(R) Core(TM) i9-14900K").Title);

    [Fact]
    public void UnknownCpu_GenericGuide() => Assert.Contains("general", BiosGuides.For("Mystery CPU").Title, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void EveryGuide_HasStepsAndWarnings() =>
        Assert.All(BiosGuides.All, g => { Assert.NotEmpty(g.Steps); Assert.NotEmpty(g.Warnings); });
}
