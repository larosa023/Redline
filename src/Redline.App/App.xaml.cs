using Microsoft.UI.Xaml;
using Redline.App.Theming;

namespace Redline.App;

public partial class App : Application
{
    MainWindow? _window;

    public App()
    {
        InitializeComponent();
        ThemeBrushes.Register(Resources);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var demo = Environment.GetCommandLineArgs().Contains("--demo");
        _window = new MainWindow(demo);
        _window.Activate();
    }
}
