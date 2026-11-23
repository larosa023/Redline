using Redline.Core.Drivers;
using Redline.Core.Gpu;
using Xunit;

namespace Redline.Core.Tests.Drivers;

public class DriverPolicyTests
{
    [Fact]
    public void Nvidia_BelowMin_TooOld()
    {
        var v = DriverPolicy.Check(Vendor.Nvidia, "552.44");
        Assert.Equal(DriverStatus.TooOld, v.Status);
        Assert.NotNull(v.Message);
    }

    [Fact]
    public void Nvidia_Current_Ok() => Assert.Equal(DriverStatus.Ok, DriverPolicy.Check(Vendor.Nvidia, "581.42").Status);

    [Fact]
    public void KnownBad_ReturnsReason()
    {
        var data = new Dictionary<Vendor, VendorDriverPolicy>
        {
            [Vendor.Nvidia] = new("555.85", [new KnownBadDriver("566.03", "Fan control resets after sleep")]),
        };
        var v = DriverPolicy.Check(Vendor.Nvidia, "566.03", data);
        Assert.Equal(DriverStatus.KnownBad, v.Status);
        Assert.False(string.IsNullOrWhiteSpace(v.Message));
    }

    [Fact]
    public void Unparseable_Unknown() => Assert.Equal(DriverStatus.Unknown, DriverPolicy.Check(Vendor.Amd, "beta-xyz").Status);

    [Fact]
    public void Intel_FourPartCompare()
    {
        Assert.Equal(DriverStatus.Ok, DriverPolicy.Check(Vendor.Intel, "32.0.101.6000").Status);
        Assert.Equal(DriverStatus.TooOld, DriverPolicy.Check(Vendor.Intel, "31.0.101.4502").Status);
    }

    [Fact]
    public void Simulated_AlwaysOk() => Assert.Equal(DriverStatus.Ok, DriverPolicy.Check(Vendor.Simulated, "1.0").Status);
}
