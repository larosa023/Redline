using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Redline.App.ViewModels;

namespace Redline.App.Pages;

public sealed partial class SettingsPage : UserControl
{
    readonly ShellViewModel _vm;
    bool _loading;

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
        _loading = false;
    }

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

    async void OnRate(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (_loading || double.IsNaN(e.NewValue)) return;
        await _vm.UpdateSettingsAsync(s => s with { ElectricityRatePerKwh = (decimal)e.NewValue });
    }
}
