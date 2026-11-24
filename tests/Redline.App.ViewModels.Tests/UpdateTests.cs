using Redline.App.ViewModels;
using Redline.Ipc;
using Xunit;

namespace Redline.App.ViewModels.Tests;

public class UpdateTests
{
    sealed class FakeUpdates : IUpdateSource
    {
        public UpdateOffer? Offer;
        public bool Applied;
        public Task<UpdateOffer?> CheckAsync() => Task.FromResult(Offer);
        public Task DownloadAndRestartAsync(UpdateOffer o, IProgress<int> progress) { progress.Report(100); Applied = true; return Task.CompletedTask; }
    }

    [Fact]
    public async Task UpdateViewModel_NoUpdate_Silent()
    {
        var vm = new UpdateViewModel(new FakeUpdates(), FakeHelper.Inline);
        await vm.CheckAsync();
        Assert.False(vm.Available);
    }

    [Fact]
    public async Task UpdateAvailable_ShowsNotes_AndApplies()
    {
        var src = new FakeUpdates { Offer = new UpdateOffer("1.0.1", "Fixes fan curve on RTX 50 cards.") };
        var vm = new UpdateViewModel(src, FakeHelper.Inline);
        await vm.CheckAsync();
        Assert.True(vm.Available);
        Assert.Equal("Redline 1.0.1 is ready", vm.Title);
        Assert.Equal("Fixes fan curve on RTX 50 cards.", vm.Notes);
        await vm.UpdateCommand.ExecuteAsync();
        Assert.True(src.Applied);
    }

    [Fact]
    public async Task CheckFailure_StaysSilent()
    {
        var vm = new UpdateViewModel(new ThrowingUpdates(), FakeHelper.Inline);
        await vm.CheckAsync();
        Assert.False(vm.Available);
    }

    sealed class ThrowingUpdates : IUpdateSource
    {
        public Task<UpdateOffer?> CheckAsync() => throw new HttpRequestException("offline");
        public Task DownloadAndRestartAsync(UpdateOffer o, IProgress<int> p) => Task.CompletedTask;
    }

    [Fact]
    public async Task HelperVersionMismatch_RequestsSetup()
    {
        var h = new FakeHelper();
        h.State = h.State with { HelperVersion = "0.9.0" };
        var s = new ShellViewModel(h, FakeHelper.Inline) { AppVersion = "1.0.0" };
        await s.LoadAsync();
        Assert.True(s.HelperOutdated);
    }

    [Fact]
    public async Task PrereleaseVersions_SameBuild_NoPrompt()
    {
        var h = new FakeHelper();
        h.State = h.State with { HelperVersion = "0.9.0-beta.2" };
        var s = new ShellViewModel(h, FakeHelper.Inline) { AppVersion = "0.9.0-beta.2+887db3f" };
        await s.LoadAsync();
        Assert.False(s.HelperOutdated);
    }

    [Fact]
    public void DefaultAppVersion_UsesInformationalVersion()
    {
        var info = typeof(ShellViewModel).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion.Split('+')[0];
        Assert.Equal(info, new ShellViewModel(new FakeHelper(), FakeHelper.Inline).AppVersion);
    }

    [Fact]
    public async Task HelperVersionMatch_NoPrompt()
    {
        var s = new ShellViewModel(new FakeHelper(), FakeHelper.Inline) { AppVersion = "1.0.0" };
        await s.LoadAsync();
        Assert.False(s.HelperOutdated);
    }
}
