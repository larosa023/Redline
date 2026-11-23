using Redline.Ipc;

namespace Redline.App.ViewModels;

/// <summary>The app's connection to Redline Helper.</summary>
public interface IHelperClient
{
    Task ConnectAsync(TimeSpan timeout);
    Task<IpcMessage> RequestAsync(string type, object? payload);
    event Action<IpcMessage>? EventReceived;
    event Action? Disconnected;
    bool Connected { get; }
}

public sealed class HelperClient(string pipeName = PipeProtocol.PipeName) : IHelperClient, IAsyncDisposable
{
    static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    PipeClient? _pipe;

    public event Action<IpcMessage>? EventReceived;
    public event Action? Disconnected;
    public bool Connected => _pipe?.Connected ?? false;

    public async Task ConnectAsync(TimeSpan timeout)
    {
        if (_pipe is not null) await _pipe.DisposeAsync();
        _pipe = await PipeClient.ConnectAsync(pipeName, timeout);
        _pipe.EventReceived += m => EventReceived?.Invoke(m);
        _pipe.Disconnected += () => Disconnected?.Invoke();
    }

    public Task<IpcMessage> RequestAsync(string type, object? payload) =>
        (_pipe ?? throw new HelperUnavailableException("Redline Helper isn't running.")).RequestAsync(type, payload, RequestTimeout);

    public async ValueTask DisposeAsync() { if (_pipe is not null) await _pipe.DisposeAsync(); }
}
