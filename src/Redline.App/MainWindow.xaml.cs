using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Redline.App.Pages;
using Redline.App.Services;
using Redline.App.Theming;
using Redline.App.ViewModels;
using Redline.App.ViewModels.Theming;

namespace Redline.App;

public sealed partial class MainWindow : Window
{
    readonly HelperLauncher _launcher;
    readonly Dictionary<string, FrameworkElement> _pages = new();
    readonly Dictionary<string, Button> _navButtons = new();
    string _current = "Home";

    public ShellViewModel Vm { get; }

    public MainWindow(bool demo)
    {
        _launcher = new HelperLauncher(demo);
        var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Vm = new ShellViewModel(new HelperClient(_launcher.PipeName), a => { if (!dq.TryEnqueue(() => a())) a(); });
        InitializeComponent();

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "redline.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 900));
        if (AppWindow.Presenter is OverlappedPresenter op) { op.PreferredMinimumWidth = 760; op.PreferredMinimumHeight = 600; }

        _pages["Home"] = new HomePage(Vm);
        _pages["Tune"] = new TunePage(Vm);
        _pages["Fans"] = new FansPage(Vm);
        _pages["Auto-tune"] = new AutoTunePage(Vm);
        _pages["Settings"] = new SettingsPage(Vm);
        foreach (var name in _pages.Keys) AddNav(name);
        Navigate("Home");
        BuildSwatches();

        Vm.PropertyChanged += OnVmChanged;
        Vm.Toasts.CollectionChanged += (_, _) => RenderToasts();
        Vm.RepairRequested += async () => { if (await _launcher.RepairAsync()) await Vm.LoadAsync(); };
        ThemeBrushes.Changed += () => { Root.RequestedTheme = ThemeBrushes.ElementTheme; UpdateNav(); BuildSwatches(); };
        Root.RequestedTheme = ThemeBrushes.ElementTheme;
        Closed += async (_, _) => await _launcher.DisposeAsync();
        _ = StartAsync();
    }

    async Task StartAsync()
    {
        await _launcher.EnsureStartedAsync();
        await Vm.LoadAsync();
    }

    void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.Theme)) ThemeBrushes.Apply(Themes.Get(Vm.Theme));
    }

    void AddNav(string name)
    {
        var b = new Button { Content = name, Padding = new Thickness(16, 8, 16, 8), CornerRadius = new CornerRadius(999), BorderThickness = new Thickness(0), MinHeight = 36 };
        b.Click += (_, _) => Navigate(name);
        _navButtons[name] = b;
        Nav.Children.Add(b);
    }

    void Navigate(string name)
    {
        _current = name;
        PageHost.Content = _pages[name];
        UpdateNav();
    }

    void UpdateNav()
    {
        foreach (var (name, b) in _navButtons)
        {
            var on = name == _current;
            b.Background = on ? ThemeBrushes.Text : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            b.Foreground = on ? ThemeBrushes.Bg : ThemeBrushes.Muted;
            b.FontWeight = on ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        }
    }

    void BuildSwatches()
    {
        Swatches.Children.Clear();
        ThemeName.Text = ThemeBrushes.Current.Name;
        foreach (var t in Themes.All)
        {
            var fill = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 1) };
            fill.GradientStops.Add(new GradientStop { Color = ThemeBrushes.C(t.Bg), Offset = 0.5 });
            fill.GradientStops.Add(new GradientStop { Color = ThemeBrushes.C(t.Accent), Offset = 0.5 });
            var b = new Button
            {
                Width = 30, Height = 30, Padding = new Thickness(3), CornerRadius = new CornerRadius(15),
                BorderThickness = new Thickness(2), BorderBrush = t.Key == ThemeBrushes.Current.Key ? ThemeBrushes.Text : ThemeBrushes.Line,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Content = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = fill },
            };
            AutomationProperties.SetName(b, $"{t.Name} theme");
            ToolTipService.SetToolTip(b, t.Name);
            var key = t.Key;
            b.Click += (_, _) => { ThemeBrushes.Apply(t); _ = Vm.SetThemeAsync(key); };
            Swatches.Children.Add(b);
        }
    }

    void RenderToasts()
    {
        ToastHost.Children.Clear();
        foreach (var t in Vm.Toasts.TakeLast(3))
        {
            var bar = new InfoBar
            {
                IsOpen = true, Title = t.Title, Message = t.Message,
                Severity = t.Severity switch { "warning" => InfoBarSeverity.Warning, "error" => InfoBarSeverity.Error, _ => InfoBarSeverity.Informational },
            };
            var item = t;
            bar.Closed += (_, _) => Vm.Toasts.Remove(item);
            ToastHost.Children.Add(bar);
        }
    }
}
