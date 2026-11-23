using System.Runtime.Versioning;
using Microsoft.Extensions.Hosting;
using Redline.Ipc;

namespace Redline.Helper;

/// <summary>Hosts the engine and the pipe. Service stop keeps the current tune (Windows is usually shutting down, which clears it anyway).</summary>
[SupportedOSPlatform("windows")]
public sealed class HelperService(string dataDir, bool demo, string pipeName) : BackgroundService
{
    readonly List<IDisposable> _owned = new();
    Engine.Engine? _engine;
    PipeServer? _server;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(dataDir);
        _engine = Composition.CreateEngine(dataDir, demo, _owned);
        await _engine.StartAsync(stoppingToken);
        _server = new PipeServer(pipeName, _engine, demo ? null : InstallLayout.CreatePipe);
        _server.Start();
        try { await Task.Delay(Timeout.Infinite, stoppingToken); } catch (OperationCanceledException) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_server is not null) await _server.DisposeAsync();
        if (_engine is not null) { await _engine.StopAsync(keepTune: true); await _engine.DisposeAsync(); }
        foreach (var d in _owned) d.Dispose();
    }
}
