using Redline.Ipc;
using Xunit;

namespace Redline.Engine.Tests;

public class PipeTests
{
    [Fact]
    public async Task Client_NoServer_TimesOutWithin2s()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<HelperUnavailableException>(() => PipeClient.ConnectAsync("redline-test-" + Guid.NewGuid().ToString("N"), TimeSpan.FromSeconds(1)));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"took {sw.Elapsed}");
    }

    [Fact]
    public async Task PipeRoundTrip_GetState()
    {
        await using var f = new EngineFixture();
        await f.StartAsync();
        var name = "redline-test-" + Guid.NewGuid().ToString("N");
        await using var server = new PipeServer(name, f.Engine);
        server.Start();
        await using var client = await PipeClient.ConnectAsync(name, TimeSpan.FromSeconds(5));
        var resp = await client.RequestAsync(MessageTypes.GetState, null, TimeSpan.FromSeconds(5));
        Assert.Equal(MessageTypes.Ok, resp.Type);
        Assert.Single(resp.PayloadAs<HelperState>()!.Devices);
    }

    [Fact]
    public async Task Pipe_SubscribeTelemetry_ReceivesEvents()
    {
        await using var f = new EngineFixture();
        await f.StartAsync();
        var name = "redline-test-" + Guid.NewGuid().ToString("N");
        await using var server = new PipeServer(name, f.Engine);
        server.Start();
        await using var client = await PipeClient.ConnectAsync(name, TimeSpan.FromSeconds(5));
        var got = new TaskCompletionSource<IpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.EventReceived += m => { if (m.Type == MessageTypes.Telemetry) got.TrySetResult(m); };
        await client.RequestAsync(MessageTypes.SubscribeTelemetry, null, TimeSpan.FromSeconds(5));
        Assert.Equal(1, f.Engine.SubscriberCount);
        f.Time.Advance(TimeSpan.FromSeconds(1));
        var ev = await got.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("gpu0", ev.PayloadAs<TelemetryEvent>()!.DeviceId);
    }
}
