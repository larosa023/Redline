using Redline.App.ViewModels;
using Redline.Core.Gpu;
using Redline.Ipc;
using Xunit;

namespace Redline.App.ViewModels.Tests;

public class VendorTests
{
    static async Task<(ShellViewModel, FakeHelper)> Shell(Vendor v, bool waiver)
    {
        var h = new FakeHelper();
        h.State = h.State with { Devices = [h.State.Devices[0] with { Vendor = v, IntelWaiverAccepted = waiver, Name = v + " card" }] };
        var s = new ShellViewModel(h, FakeHelper.Inline) { Delay = HelperRecoveryTests.InstantThenNever(0) };
        await s.LoadAsync();
        return (s, h);
    }

    [Fact]
    public async Task Intel_WithoutWaiver_AsksForIt_AndSendsAcceptance()
    {
        var (s, h) = await Shell(Vendor.Intel, waiver: false);
        Assert.True(s.Tune.NeedsWaiver);
        await s.Tune.AcceptWaiverCommand.ExecuteAsync();
        Assert.Contains(h.Sent, m => m.Type == MessageTypes.AcceptIntelWaiver && ((DeviceRequest)m.Payload!).DeviceId == "gpu0");
    }

    [Fact]
    public async Task Nvidia_NoWaiver_NoPreviewNote()
    {
        var (s, _) = await Shell(Vendor.Nvidia, waiver: true);
        Assert.False(s.Tune.NeedsWaiver);
        Assert.Null(s.Tune.VendorNote);
    }

    [Theory]
    [InlineData(Vendor.Amd)]
    [InlineData(Vendor.Intel)]
    public async Task AmdIntel_ShowPreviewNote(Vendor v) => Assert.NotNull((await Shell(v, waiver: true)).Item1.Tune.VendorNote);
}
