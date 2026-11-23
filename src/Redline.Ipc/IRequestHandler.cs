namespace Redline.Ipc;

/// <summary>What the pipe server talks to (the engine).</summary>
public interface IRequestHandler
{
    Task<IpcMessage> HandleAsync(IpcMessage req, CancellationToken ct);
    event Action<IpcMessage>? Event;
    int SubscriberCount { get; set; }
}

public sealed class HelperUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
