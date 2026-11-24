using Redline.App.ViewModels;
using Redline.Ipc;
using Xunit;

namespace Redline.App.ViewModels.Tests;

/// <summary>beta.4: after "Update helper" the new helper took longer to start than the app waited, so the
/// setup screen stayed up and every click reinstalled (and restarted) the helper. These pin the recovery.</summary>
public class HelperRecoveryTests
{
    sealed class Gate
    {
        readonly SemaphoreSlim _s = new(0);
        public int Waits;
        public Task Delay(TimeSpan _) { Interlocked.Increment(ref Waits); return _s.WaitAsync(); }
        public void Release() => _s.Release();
    }

    /// <summary>Instant for the first <paramref name="n"/> waits, then never returns (stops background retry loops in tests).</summary>
    internal static Func<TimeSpan, Task> InstantThenNever(int n)
    {
        var count = 0;
        return _ => Interlocked.Increment(ref count) <= n ? Task.CompletedTask : new TaskCompletionSource().Task;
    }

    static async Task Until(Func<bool> cond)
    {
        for (var i = 0; i < 200 && !cond(); i++) await Task.Delay(10);
        Assert.True(cond());
    }

    [Fact]
    public async Task Repair_WaitsForSlowHelper_ThenConnects()
    {
        var h = new FakeHelper();
        var s = new ShellViewModel(h, FakeHelper.Inline) { AppVersion = "1.0.0" };
        var waits = 0;
        s.Delay = _ => { if (++waits == 4) h.Available = true; return Task.CompletedTask; };
        var installs = 0;
        s.Installer = () => { installs++; h.Available = false; return Task.FromResult(true); };

        await s.RepairCommand.ExecuteAsync();

        Assert.Equal(1, installs);
        Assert.False(s.HelperMissing);
        Assert.False(s.Repairing);
        Assert.NotNull(s.State);
        Assert.Null(s.LastError);
    }

    [Fact]
    public async Task Repair_CannotRunTwiceAtOnce()
    {
        var h = new FakeHelper { Available = false };
        var s = new ShellViewModel(h, FakeHelper.Inline);
        var gate = new Gate();
        s.Delay = gate.Delay;
        var install = new TaskCompletionSource<bool>();
        var installs = 0;
        s.Installer = () => { installs++; return install.Task; };

        var first = s.RepairCommand.ExecuteAsync();
        Assert.True(s.Repairing);
        Assert.False(s.RepairCommand.CanExecute(null));
        s.RepairCommand.Execute(null);
        Assert.Equal(1, installs);

        h.Available = true;
        install.SetResult(true);
        await first;
        Assert.False(s.Repairing);
        Assert.False(s.HelperMissing);
    }

    [Fact]
    public async Task Repair_Declined_ShowsMessage()
    {
        var s = new ShellViewModel(new FakeHelper { Available = false }, FakeHelper.Inline) { Delay = InstantThenNever(0) };
        s.Installer = () => Task.FromResult(false);
        await s.RepairCommand.ExecuteAsync();
        Assert.False(s.Repairing);
        Assert.NotNull(s.LastError);
    }

    [Fact]
    public async Task Repair_HelperNeverStarts_GivesUpWithMessage()
    {
        var h = new FakeHelper();
        var s = new ShellViewModel(h, FakeHelper.Inline) { Delay = InstantThenNever(1000) };
        s.Installer = () => { h.Available = false; return Task.FromResult(true); };
        await s.RepairCommand.ExecuteAsync();
        Assert.True(s.HelperMissing);
        Assert.False(s.Repairing);
        Assert.Contains("install.log", s.LastError);
    }

    [Fact]
    public async Task Disconnect_ReconnectsOnItsOwn_WhenHelperComesBack()
    {
        var h = new FakeHelper();
        var gate = new Gate();
        var s = new ShellViewModel(h, FakeHelper.Inline) { Delay = gate.Delay };
        await s.LoadAsync();
        h.Drop();
        Assert.True(s.HelperMissing);

        await Until(() => gate.Waits == 1);
        gate.Release();                       // still down: keeps trying
        await Until(() => gate.Waits == 2);
        h.Available = true;
        gate.Release();
        await Until(() => !s.HelperMissing);
        Assert.Equal(2, gate.Waits);
    }

    [Fact]
    public async Task StartWithoutHelper_ReconnectsOnItsOwn()
    {
        var h = new FakeHelper { Available = false };
        var gate = new Gate();
        var s = new ShellViewModel(h, FakeHelper.Inline) { Delay = gate.Delay };
        await s.LoadAsync();
        Assert.True(s.HelperMissing);
        await Until(() => gate.Waits == 1);
        h.Available = true;
        gate.Release();
        await Until(() => !s.HelperMissing && s.State is not null);   // the retry runs in the background
    }

    sealed class EchoHandler : IRequestHandler
    {
        public event Action<IpcMessage>? Event { add { } remove { } }
        public int SubscriberCount { get; set; }
        public Task<IpcMessage> HandleAsync(IpcMessage req, CancellationToken ct) => Task.FromResult(new IpcMessage(req.Id, MessageTypes.Ok, null));
    }

    [Fact]
    public async Task HelperClient_Reconnect_DoesNotReportOldPipeAsDisconnected()
    {
        var name = $"Redline.Test.{Guid.NewGuid():N}";
        await using var server = new PipeServer(name, new EchoHandler());
        server.Start();
        await using var client = new HelperClient(name);
        var drops = 0;
        client.Disconnected += () => Interlocked.Increment(ref drops);

        await client.ConnectAsync(TimeSpan.FromSeconds(5));
        await client.ConnectAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(200);

        Assert.Equal(0, drops);
        Assert.True(client.Connected);
    }
}
