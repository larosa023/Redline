namespace Redline.Ipc;

/// <summary>
/// Lets the helper open its pipe before the hardware is ready: requests wait until <see cref="Ready"/> hands over
/// the engine, so the app connects at once instead of showing the setup screen while the helper starts.
/// </summary>
public sealed class StartupGate : IRequestHandler
{
    readonly TaskCompletionSource<IRequestHandler> _engine = new(TaskCreationOptions.RunContinuationsAsynchronously);
    int _subscribers;

    public event Action<IpcMessage>? Event;

    public int SubscriberCount
    {
        get => _engine.Task.IsCompletedSuccessfully ? _engine.Task.Result.SubscriberCount : _subscribers;
        set { if (_engine.Task.IsCompletedSuccessfully) _engine.Task.Result.SubscriberCount = value; else _subscribers = value; }
    }

    public void Ready(IRequestHandler engine)
    {
        engine.SubscriberCount = _subscribers;
        engine.Event += m => Event?.Invoke(m);
        _engine.TrySetResult(engine);
    }

    public async Task<IpcMessage> HandleAsync(IpcMessage req, CancellationToken ct) =>
        await (await _engine.Task.WaitAsync(ct)).HandleAsync(req, ct);
}
