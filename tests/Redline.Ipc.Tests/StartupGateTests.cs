using Redline.Ipc;
using Xunit;

namespace Redline.Ipc.Tests;

public class StartupGateTests
{
    sealed class Echo : IRequestHandler
    {
        public event Action<IpcMessage>? Event;
        public int SubscriberCount { get; set; }
        public Task<IpcMessage> HandleAsync(IpcMessage req, CancellationToken ct) => Task.FromResult(new IpcMessage(req.Id, MessageTypes.Ok, null));
        public void Raise() => Event?.Invoke(new IpcMessage(0, MessageTypes.State, null));
    }

    [Fact]
    public async Task RequestsWait_UntilTheEngineIsReady_ThenGoThrough()
    {
        var gate = new StartupGate();
        var pending = gate.HandleAsync(new IpcMessage(7, MessageTypes.GetState, null), default);
        await Task.Delay(50);
        Assert.False(pending.IsCompleted);

        gate.SubscriberCount = 1;
        var echo = new Echo();
        gate.Ready(echo);
        var resp = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal((7, MessageTypes.Ok), (resp.Id, resp.Type));
        Assert.Equal(1, echo.SubscriberCount);       // subscriptions made while starting carry over
    }

    [Fact]
    public void EngineEvents_ReachThePipeServer_AfterReady()
    {
        var gate = new StartupGate();
        var got = 0;
        gate.Event += _ => got++;
        var echo = new Echo();
        gate.Ready(echo);
        echo.Raise();
        Assert.Equal(1, got);
    }
}
