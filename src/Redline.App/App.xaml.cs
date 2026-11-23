using Microsoft.UI.Xaml;
using Redline.App.Theming;

namespace Redline.App;

public partial class App : Application
{
    MainWindow? _window;

    public App()
    {
        UnhandledException += (_, e) => { CrashLog.Write("xaml", e.Exception, show: true); };
        CrashLog.Write("app ctor", null, show: false);
        InitializeComponent();
        CrashLog.Write("app InitializeComponent done", null, show: false);
        ThemeBrushes.Register(Resources);
        CrashLog.Write("brushes registered", null, show: false);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            CrashLog.Write("OnLaunched", null, show: false);
            var demo = Environment.GetCommandLineArgs().Contains("--demo");
            _window = new MainWindow(demo);
            _window.Activate();
            CrashLog.Write("started", null, show: false);
        }
        catch (Exception e)
        {
            CrashLog.Write("window", e, show: true);
            Exit();
        }
    }
}
