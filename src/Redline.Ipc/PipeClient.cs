using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Redline.Ipc;

/// <summary>App side of the helper pipe: request/response by id, plus a stream of events.</summary>
public sealed class PipeClient : IAsyncDisposable
{
    readonly NamedPipeClientStream _pipe;
    readonly StreamWriter _writer;
    readonly SemaphoreSlim _writeLock = new(1, 1);
    readonly ConcurrentDictionary<int, TaskCompletionSource<IpcMessage>> _waiting = new();
    readonly CancellationTokenSource _cts = new();
    readonly Task _reader;
    int _nextId;

    PipeClient(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
        _writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        _reader = Task.Run(ReadLoopAsync);
    }

    public event Action<IpcMessage>? EventReceived;
    public event Action? Disconnected;
    public bool Connected => _pipe.IsConnected && !_reader.IsCompleted;

    public static Task<PipeClient> ConnectAsync(TimeSpan timeout) => ConnectAsync(PipeProtocol.PipeName, timeout);

    public static async Task<PipeClient> ConnectAsync(string pipeName, TimeSpan timeout)
    {
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            await pipe.ConnectAsync(cts.Token);
            return new PipeClient(pipe);
        }
        catch (Exception e) when (e is OperationCanceledException or TimeoutException or IOException or UnauthorizedAccessException)
        {
            await pipe.DisposeAsync();
            throw new HelperUnavailableException("Redline Helper isn't running.", e);
        }
    }

    public Task<IpcMessage> RequestAsync<T>(string type, T payload, TimeSpan timeout) =>
        SendAsync(id => IpcMessage.Create(id, type, payload), timeout);

    public Task<IpcMessage> RequestAsync(string type, object? payload, TimeSpan timeout) =>
        SendAsync(id => payload is null ? new IpcMessage(id, type, null)
            : new IpcMessage(id, type, JsonSerializer.SerializeToElement(payload, payload.GetType(), PipeProtocol.Options)), timeout);

    async Task<IpcMessage> SendAsync(Func<int, IpcMessage> make, TimeSpan timeout)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<IpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiting[id] = tcs;
        try
        {
            await _writeLock.WaitAsync();
            try { await _writer.WriteLineAsync(PipeProtocol.Serialize(make(id))); }
            finally { _writeLock.Release(); }
            return await tcs.Task.WaitAsync(timeout);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or TimeoutException)
        {
            throw new HelperUnavailableException("Lost connection to Redline Helper.", e);
        }
        finally { _waiting.TryRemove(id, out _); }
    }

    async Task ReadLoopAsync()
    {
        using var reader = new StreamReader(_pipe, Encoding.UTF8);
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(_cts.Token);
                if (line is null) break;
                IpcMessage m;
                try { m = PipeProtocol.Deserialize(line); } catch (IpcProtocolException) { continue; }
                if ((m.Type is MessageTypes.Ok or MessageTypes.ErrorResponse) && _waiting.TryRemove(m.Id, out var tcs)) tcs.TrySetResult(m);
                else EventReceived?.Invoke(m);
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
        foreach (var w in _waiting.Values) w.TrySetException(new IOException("Disconnected"));
        Disconnected?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        await _pipe.DisposeAsync();
        try { await _reader; } catch (Exception) { }
    }
}
