using System.ComponentModel;
using System.Diagnostics;
using Redline.Ipc;

namespace Redline.App.Services;

/// <summary>
/// Finds and starts Redline Helper. Normal mode: the installed service (Repair runs the installer elevated).
/// Demo mode (--demo): a private helper with a simulated GPU, no admin rights needed.
/// </summary>
public sealed class HelperLauncher(bool demo) : IAsyncDisposable
{
    Process? _demo;

    public string PipeName { get; } = demo ? $"Redline.Demo.{Environment.ProcessId}" : PipeProtocol.PipeName;

    static string HelperExe => Path.Combine(AppContext.BaseDirectory, "helper", "Redline.Helper.exe");

    public Task EnsureStartedAsync()
    {
        if (!demo || _demo is not null || !File.Exists(HelperExe)) return Task.CompletedTask;
        _demo = Process.Start(new ProcessStartInfo(HelperExe, $"--demo --pipe {PipeName}") { CreateNoWindow = true, UseShellExecute = false });
        return Task.Delay(1500);
    }

    /// <summary>Installs or repairs the helper service (one UAC prompt). Returns false if the user declined.</summary>
    public async Task<bool> RepairAsync()
    {
        if (demo) { await EnsureStartedAsync(); return true; }
        if (!File.Exists(HelperExe)) return false;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(HelperExe, $"--install \"{Path.GetDirectoryName(HelperExe)}\"") { UseShellExecute = true, Verb = "runas" })!;
            await p.WaitForExitAsync();
            return p.ExitCode == 0;   // the app then waits for the helper to answer (it can take a while to load drivers)
        }
        catch (Win32Exception) { return false; }   // user said no to the prompt
    }

    public ValueTask DisposeAsync()
    {
        try { _demo?.Kill(); } catch (InvalidOperationException) { }
        _demo?.Dispose();
        return ValueTask.CompletedTask;
    }
}
