using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;

namespace Redline.App;

public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => UninstallHelper())
            .Run();

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            var ctx = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(ctx);
            _ = new App();
        });
    }

    /// <summary>Removes the helper service when Redline is uninstalled (one admin prompt). Settings in ProgramData are kept.</summary>
    static void UninstallHelper()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "helper", "Redline.Helper.exe");
        if (!File.Exists(exe)) return;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, "--uninstall") { UseShellExecute = true, Verb = "runas" });
            p?.WaitForExit(30_000);
        }
        catch (Exception) { /* declined: the service stays until removed manually */ }
    }
}
