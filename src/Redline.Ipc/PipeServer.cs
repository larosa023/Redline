using System.IO.Pipes;
using System.Text;

namespace Redline.Ipc;

/// <summary>Accepts app connections on the helper pipe. Responses go to the asking connection; events go to all (telemetry only to subscribers).</summary>
public sealed class PipeServer(string pipeName, IRequestHandler handler, Func<string, NamedPipeServerStream>? createPipe = null) : IAsyncDisposable
{
    sealed class Conn(NamedPipeServerStream pipe)
    {
        public readonly NamedPipeServerStream Pipe = pipe;
        public readonly StreamWriter Writer = new(pipe, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        public readonly SemaphoreSlim WriteLock = new(1, 1);
        public bool Subscribed;
    }

    readonly CancellationTokenSource _cts = new();
    readonly List<Conn> _conns = new();
    readonly List<Task> _tasks = new();
    Task? _accept;

    /// <summary>Raised when a pipe instance couldn't be created (the loop retries every second).</summary>
    public event Action<Exception>? AcceptError;

    public void Start()
    {
        handler.Event += Broadcast;
        _accept = Task.Run(AcceptLoopAsync);
    }

    NamedPipeServerStream NewPipe() => createPipe?.Invoke(pipeName)
        ?? new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try { pipe = NewPipe(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Never let the accept loop die quietly: the helper would look "ready" but accept nobody.
                AcceptError?.Invoke(e);
                try { await Task.Delay(1000, _cts.Token); } catch (OperationCanceledException) { return; }
                continue;
            }
            try { await pipe.WaitForConnectionAsync(_cts.Token); }
            catch (OperationCanceledException) { await pipe.DisposeAsync(); return; }
            catch (IOException) { await pipe.DisposeAsync(); continue; }
            var c = new Conn(pipe);
            lock (_conns) _conns.Add(c);
            lock (_tasks) _tasks.Add(Task.Run(() => ServeAsync(c)));
        }
    }

    async Task ServeAsync(Conn c)
    {
        using var reader = new StreamReader(c.Pipe, Encoding.UTF8);
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(_cts.Token);
                if (line is null) break;
                IpcMessage resp;
                try
                {
                    var req = PipeProtocol.Deserialize(line);
                    if (req.Type == MessageTypes.SubscribeTelemetry && !c.Subscribed) { c.Subscribed = true; handler.SubscriberCount++; }
                    if (req.Type == MessageTypes.UnsubscribeTelemetry && c.Subscribed) { c.Subscribed = false; handler.SubscriberCount--; }
                    resp = await handler.HandleAsync(req, _cts.Token);
                }
                catch (IpcProtocolException e) { resp = IpcMessage.Error(0, e.Message); }
                await SendAsync(c, resp);
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally
        {
            lock (_conns) _conns.Remove(c);
            if (c.Subscribed) handler.SubscriberCount--;
            await c.Pipe.DisposeAsync();
        }
    }

    static async Task SendAsync(Conn c, IpcMessage m)
    {
        await c.WriteLock.WaitAsync();
        try { await c.Writer.WriteLineAsync(PipeProtocol.Serialize(m)); }
        catch (Exception e) when (e is IOException or ObjectDisposedException) { }
        finally { c.WriteLock.Release(); }
    }

    void Broadcast(IpcMessage m)
    {
        Conn[] targets;
        lock (_conns) targets = _conns.Where(c => m.Type != MessageTypes.Telemetry || c.Subscribed).ToArray();
        foreach (var c in targets) _ = SendAsync(c, m);
    }

    public async ValueTask DisposeAsync()
    {
        handler.Event -= Broadcast;
        _cts.Cancel();
        Conn[] all;
        lock (_conns) all = _conns.ToArray();
        foreach (var c in all) await c.Pipe.DisposeAsync();
        try { if (_accept is not null) await _accept; } catch (Exception) { }
        Task[] ts;
        lock (_tasks) ts = _tasks.ToArray();
        try { await Task.WhenAll(ts); } catch (Exception) { }
    }
}
