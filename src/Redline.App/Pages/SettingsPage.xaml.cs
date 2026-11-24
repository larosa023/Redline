using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Redline.App.Theming;
using Windows.System;
using Microsoft.UI.Xaml.Controls.Primitives;
using Redline.App.ViewModels;

namespace Redline.App.Pages;

public sealed partial class SettingsPage : UserControl
{
    readonly ShellViewModel _vm;
    bool _loading;
    string? _capturing;

    public SettingsPage(ShellViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ShellViewModel.State)) Load(); };
        Load();
    }

    void Load()
    {
        if (_vm.State is not { } s) return;
        _loading = true;
        Ceiling.Value = s.Settings.TempCeilingC;
        PowerOptIn.IsOn = s.Settings.Safety.AllowPowerAbove100;
        Rate.Value = (double)s.Settings.ElectricityRatePerKwh;
        Overlay.IsOn = s.Settings.OverlayEnabled;
        Corner.SelectedItem = s.Settings.OverlayCorner;
        Version.Text = $"Redline {typeof(SettingsPage).Assembly.GetName().Version?.ToString(3)} · Helper {s.HelperVersion}";
        if (_capturing is null) BuildShortcuts();
        _loading = false;
    }

    void BuildShortcuts()
    {
        Shortcuts.Children.Clear();
        if (_vm.State is not { } s) return;
        foreach (var p in s.Profiles)
        {
            var id = p.Id;
            var current = s.Settings.Hotkeys.TryGetValue(id, out var g) ? g : null;
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = p.Name, VerticalAlignment = VerticalAlignment.Center, Foreground = ThemeBrushes.Text });
            var set = new Button { Content = current ?? "Set shortcut", MinWidth = 160 };
            set.Click += (_, _) => { _capturing = id; set.Content = "Press keys…"; };
            set.LostFocus += (_, _) => { if (_capturing == id) { _capturing = null; BuildShortcuts(); } };
            set.PreviewKeyDown += async (_, e) =>
            {
                if (_capturing != id) return;
                if (e.Key == VirtualKey.Escape) { e.Handled = true; _capturing = null; BuildShortcuts(); return; }
                if (Gesture(e.Key) is not { } gesture) return;   // only a modifier so far: keep waiting
                e.Handled = true;
                _capturing = null;
                await _vm.Tray.SetHotkeyAsync(id, gesture);
                BuildShortcuts();
            };
            Grid.SetColumn(set, 1);
            row.Children.Add(set);
            if (current is not null)
            {
                var clear = new Button { Content = "Clear" };
                clear.Click += async (_, _) => { await _vm.Tray.SetHotkeyAsync(id, null); BuildShortcuts(); };
                Grid.SetColumn(clear, 2);
                row.Children.Add(clear);
            }
            Shortcuts.Children.Add(row);
        }
    }

    static string? Gesture(VirtualKey key)
    {
        string? k = key switch
        {
            >= VirtualKey.A and <= VirtualKey.Z => key.ToString(),
            >= VirtualKey.Number0 and <= VirtualKey.Number9 => ((int)key - (int)VirtualKey.Number0).ToString(),
            >= VirtualKey.F1 and <= VirtualKey.F24 => key.ToString(),
            _ => null,
        };
        if (k is null) return null;
        var parts = new List<string>();
        if (Down(VirtualKey.Control)) parts.Add("Ctrl");
        if (Down(VirtualKey.Menu)) parts.Add("Alt");
        if (Down(VirtualKey.Shift)) parts.Add("Shift");
        if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows)) parts.Add("Win");
        parts.Add(k);
        return string.Join('+', parts);
    }

    static bool Down(VirtualKey k) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(k).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    async void OnCeiling(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading) return;
        await _vm.UpdateSettingsAsync(s => s with { TempCeilingC = (int)e.NewValue });
    }

    async void OnPowerOptIn(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_loading) return;
        await _vm.UpdateSettingsAsync(s => s with { Safety = s.Safety with { AllowPowerAbove100 = PowerOptIn.IsOn } });
    }

    async void OnOverlay(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_loading) return;
        await _vm.UpdateSettingsAsync(s => s with { OverlayEnabled = Overlay.IsOn });
    }

    async void OnCorner(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || Corner.SelectedItem is not string c) return;
        await _vm.UpdateSettingsAsync(s => s with { OverlayCorner = c });
    }

    async void OnReport(object sender, RoutedEventArgs e)
    {
        ReportButton.IsEnabled = false;
        try
        {
            var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Redline");
            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var path = await _vm.Tools.CreateReportAsync(downloads, [Path.Combine(local, "logs"), local]);
            ReportMessage.Text = _vm.Tools.Message;
            if (path is not null) System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        finally { ReportButton.IsEnabled = true; }
    }

    async void OnRate(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (_loading || double.IsNaN(e.NewValue)) return;
        await _vm.UpdateSettingsAsync(s => s with { ElectricityRatePerKwh = (decimal)e.NewValue });
    }
}
