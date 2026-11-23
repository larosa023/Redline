using Microsoft.UI.Xaml;
using Redline.App.Theming;

namespace Redline.App;

public partial class App : Application
{
    MainWindow? _window;

    public App()
    {
        UnhandledException += (_, e) => { CrashLog.Write("xaml", e.Exception, show: true); };
        InitializeComponent();
        ThemeBrushes.Register(Resources);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
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
