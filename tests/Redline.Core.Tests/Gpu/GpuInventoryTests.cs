using Redline.Core.Gpu;
using Xunit;

namespace Redline.Core.Tests.Gpu;

public class GpuInventoryTests
{
    static (string PnpId, string Name) A(string ven, string name) => ($@"PCI\VEN_{ven}&DEV_1234&SUBSYS_00000000&REV_C1\4&1", name);

    [Fact]
    public void AnthonysPc_NvidiaPlusRyzenGraphics_LoadsNeitherAmdNorIntel()
    {
        var plan = GpuInventory.Plan([A("10DE", "NVIDIA GeForce RTX 4090"), A("1002", "AMD Radeon(TM) Graphics")]);
        Assert.Equal((true, false, false), (plan.Nvidia, plan.Amd, plan.Intel));
    }

    [Theory]
    [InlineData("AMD Radeon RX 7900 XTX")]
    [InlineData("AMD Radeon RX 9070 XT")]
    [InlineData("AMD Radeon PRO W7800")]
    public void DiscreteRadeon_LoadsAmd(string name) => Assert.True(GpuInventory.Plan([A("1002", name)]).Amd);

    [Theory]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", true)]
    [InlineData("Intel(R) Arc(TM) B580 Graphics", true)]
    [InlineData("Intel(R) UHD Graphics 770", false)]
    [InlineData("Intel(R) Iris(R) Xe Graphics", false)]
    public void IntelArc_LoadsIntel_IntegratedDoesNot(string name, bool load) => Assert.Equal(load, GpuInventory.Plan([A("8086", name)]).Intel);

    [Fact]
    public void NothingListed_LoadsEverything()   // Windows couldn't tell us: fall back to asking every vendor
    {
        var plan = GpuInventory.Plan([]);
        Assert.Equal((true, true, true), (plan.Nvidia, plan.Amd, plan.Intel));
    }
}
