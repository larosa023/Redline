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
    readonly OverlayWindow _overlay;
    readonly TrayHost _tray;
    bool _quitting;
    readonly Dictionary<string, FrameworkElement> _pages = new();
    readonly Dictionary<string, Button> _navButtons = new();
    string _current = "Home";

    public ShellViewModel Vm { get; }
    public UpdateViewModel Updates { get; }

    public MainWindow(bool demo)
    {
        _launcher = new HelperLauncher(demo);
        var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Vm = new ShellViewModel(new HelperClient(_launcher.PipeName), a => { if (!dq.TryEnqueue(() => a())) a(); });
        Updates = new UpdateViewModel(new VelopackUpdateSource(), a => { if (!dq.TryEnqueue(() => a())) a(); });
        CrashLog.Write("window ctor", null, show: false);
        InitializeComponent();
        CrashLog.Write("window InitializeComponent done", null, show: false);

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "redline.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 900));
        if (AppWindow.Presenter is OverlappedPresenter op) { op.PreferredMinimumWidth = 760; op.PreferredMinimumHeight = 600; }

        _pages["Home"] = new HomePage(Vm);
        CrashLog.Write("home page built", null, show: false);
        _pages["Tune"] = new TunePage(Vm);
        _pages["Fans"] = new FansPage(Vm);
        _pages["Auto-tune"] = new AutoTunePage(Vm);
        _pages["Games"] = new GamesPage(Vm);
        _pages["Stats"] = new StatsPage(Vm);
        _pages["CPU"] = new CpuPage(Vm);
        _pages["Settings"] = new SettingsPage(Vm);
        foreach (var name in _pages.Keys) AddNav(name);
        Navigate("Home");
        BuildSwatches();

        Vm.PropertyChanged += OnVmChanged;
        Vm.Toasts.CollectionChanged += (_, _) => RenderToasts();
        Vm.Installer = _launcher.RepairAsync;
        ThemeBrushes.Changed += () => { Root.RequestedTheme = ThemeBrushes.ElementTheme; UpdateNav(); BuildSwatches(); };
        Root.RequestedTheme = ThemeBrushes.ElementTheme;
        _overlay = new OverlayWindow();
        Vm.Overlay.PropertyChanged += (_, _) => _overlay.Update(Vm.Overlay.Visible, Vm.Overlay.FpsText, Vm.Overlay.StatsText, Vm.Overlay.Corner);
        var overlayTimer = DispatcherQueue.CreateTimer();
        overlayTimer.Interval = TimeSpan.FromSeconds(1);
        overlayTimer.Tick += (_, _) => Vm.Overlay.Tick(DateTimeOffset.UtcNow);
        overlayTimer.Start();
        _tray = new TrayHost(Path.Combine(AppContext.BaseDirectory, "Assets", "redline.ico"));
        _tray.OpenRequested += ShowFromTray;
        _tray.ProfilePicked += id => _ = Vm.Tray.ApplyAsync(id);
        _tray.HotkeyPressed += id => _ = Vm.Tray.OnHotkeyAsync(id);
        _tray.QuitRequested += () => _ = QuitAsync();
        Vm.Tray.Changed += SyncTray;
        AppWindow.Closing += (_, e) =>
        {
            // Spec §6.10: closing the window with a tune active keeps Redline in the tray.
            if (_quitting || Vm.Tray.OnWindowClose() == CloseAction.Exit) return;
            e.Cancel = true;
            AppWindow.Hide();
        };
        Closed += async (_, _) => { _tray.Dispose(); _overlay.Dispose(); await _launcher.DisposeAsync(); };
        _ = StartAsync();
    }

    async Task StartAsync()
    {
        await _launcher.EnsureStartedAsync();
        await Vm.LoadAsync();
        await Updates.CheckAsync();
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromHours(6);
        timer.Tick += async (_, _) => await Updates.CheckAsync();
        timer.Start();
    }

    string _hotkeysRegistered = "";

    void SyncTray()
    {
        _tray.SetMenu(Vm.Tray.MenuItems);
        _tray.SetTooltip($"Redline · {Vm.Status}");
        var key = string.Join(";", Vm.Tray.Hotkeys.Select(b => b.ProfileId + "=" + b.Gesture));
        if (key == _hotkeysRegistered) return;
        _hotkeysRegistered = key;
        var refused = _tray.SetHotkeys(Vm.Tray.Hotkeys);
        if (refused.Count > 0)
            Vm.LastError = $"{string.Join(", ", refused)} {(refused.Count == 1 ? "is" : "are")} already used by another app. Pick a different shortcut in Settings.";
    }

    void OnDismissError(object sender, RoutedEventArgs e) => Vm.LastError = null;

    void ShowFromTray()
    {
        AppWindow.Show();
        Activate();
    }

    async Task QuitAsync()
    {
        ShowFromTray();
        if (Vm.Tray.IsTuned)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "Quit Redline?",
                Content = "Going back to factory is the safe choice. If you keep your tune, it stays on the card until you restart the PC or change it.",
                PrimaryButtonText = "Back to factory",
                SecondaryButtonText = "Keep my tune",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
            };
            var r = await dialog.ShowAsync();
            if (r == ContentDialogResult.None) return;
            await Vm.Tray.QuitAsync(r == ContentDialogResult.Primary ? QuitChoice.BackToFactory : QuitChoice.KeepTune);
        }
        _quitting = true;
        Close();
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
