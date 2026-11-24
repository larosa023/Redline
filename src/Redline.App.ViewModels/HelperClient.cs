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
        var old = _pipe;
        _pipe = null;
        if (old is not null) await old.DisposeAsync();   // closing the old pipe on purpose is not a lost helper
        var pipe = await PipeClient.ConnectAsync(pipeName, timeout);
        pipe.EventReceived += m => { if (ReferenceEquals(_pipe, pipe)) EventReceived?.Invoke(m); };
        pipe.Disconnected += () => { if (ReferenceEquals(_pipe, pipe)) Disconnected?.Invoke(); };
        _pipe = pipe;
    }

    public Task<IpcMessage> RequestAsync(string type, object? payload) =>
        (_pipe ?? throw new HelperUnavailableException("Redline Helper isn't running.")).RequestAsync(type, payload, RequestTimeout);

    public async ValueTask DisposeAsync() { if (_pipe is not null) await _pipe.DisposeAsync(); }
}
