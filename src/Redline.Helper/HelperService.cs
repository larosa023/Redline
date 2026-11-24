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
        // Yield first so Windows sees the service as started right away; the hardware setup below can be slow.
        await Task.Yield();
        try
        {
            Directory.CreateDirectory(dataDir);
            using (Redline.Core.Diagnostics.HelperLog.Time("create engine")) _engine = Composition.CreateEngine(dataDir, demo, _owned);
            using (Redline.Core.Diagnostics.HelperLog.Time("start engine")) await _engine.StartAsync(stoppingToken);
            _server = new PipeServer(pipeName, _engine, demo ? null : InstallLayout.CreatePipe);
            _server.Start();
            Redline.Core.Diagnostics.HelperLog.Write("ready: pipe open");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Redline.Core.Diagnostics.HelperLog.Write($"startup failed: {e}");
            throw;
        }
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
