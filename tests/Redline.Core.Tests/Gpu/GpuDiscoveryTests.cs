using Redline.Core.Gpu;
using Redline.Core.Sim;
using Redline.Core.Tests.Fakes;
using Xunit;

namespace Redline.Core.Tests.Gpu;

public class GpuDiscoveryTests
{
    sealed class ThrowingBackend : IGpuBackend
    {
        public Vendor Vendor => Vendor.Amd;
        public bool IsAvailable() => true;
        public IReadOnlyList<IGpuDevice> Discover() => throw new InvalidOperationException("driver exploded");
    }

    sealed class UnavailableBackend : IGpuBackend
    {
        public Vendor Vendor => Vendor.Intel;
        public bool IsAvailable() => false;
        public IReadOnlyList<IGpuDevice> Discover() => throw new InvalidOperationException("should not be called");
    }

    [Fact]
    public void Discovery_OneBackendThrows_OthersStillListed()
    {
        var sim = new SimGpuBackend(new SimGpuDevice(TestGpus.Spec()));
        var r = GpuDiscovery.DiscoverAll([new ThrowingBackend(), sim]);
        Assert.Single(r.Devices);
        var err = Assert.Single(r.Errors);
        Assert.Equal(Vendor.Amd, err.Vendor);
        Assert.Contains("driver exploded", err.Error);
    }

    [Fact]
    public void Discovery_UnavailableBackend_SkippedWithoutError()
    {
        var r = GpuDiscovery.DiscoverAll([new UnavailableBackend()]);
        Assert.Empty(r.Devices);
        Assert.Empty(r.Errors);
    }

    [Fact]
    public void MonitorOnly_SupportsNothing()
    {
        foreach (var c in Enum.GetValues<Control>()) Assert.False(Capabilities.MonitorOnly.Supports(c));
    }
}
