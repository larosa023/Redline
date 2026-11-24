using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Redline.App.Theming;
using Redline.App.ViewModels;
using Redline.Core.Cpu;

namespace Redline.App.Pages;

public sealed partial class CpuPage : UserControl
{
    public ShellViewModel Vm { get; }
    bool _loading;

    public CpuPage(ShellViewModel vm)
    {
        Vm = vm;
        InitializeComponent();
        ProfilePick.ItemsSource = vm.Cpu.Profiles;
        BoostPick.ItemsSource = vm.Cpu.BoostModes.Select(CpuViewModel.Describe).Prepend("Don't change").ToList();
        vm.Cpu.PropertyChanged += (_, _) => Load();
        vm.Cpu.Profiles.CollectionChanged += (_, _) => Load();
        ThemeBrushes.Changed += BuildGuide;
        Load();
    }

    void Load()
    {
        _loading = true;
        ProfilePick.SelectedItem = Vm.Cpu.Profiles.FirstOrDefault(p => p.Id == Vm.Cpu.SelectedProfileId);
        BoostPick.SelectedIndex = Vm.Cpu.BoostMode is { } b ? (int)b + 1 : 0;
        MaxState.Value = Vm.Cpu.MaxStatePct;
        _loading = false;
        BuildGuide();
    }

    void OnProfile(object sender, SelectionChangedEventArgs e) { if (!_loading && ProfilePick.SelectedItem is ProfileChoice p) Vm.Cpu.SelectedProfileId = p.Id; }
    void OnBoost(object sender, SelectionChangedEventArgs e) { if (!_loading) Vm.Cpu.BoostMode = BoostPick.SelectedIndex <= 0 ? null : (CpuBoostMode)(BoostPick.SelectedIndex - 1); }
    void OnMax(object sender, RangeBaseValueChangedEventArgs e) { if (!_loading) Vm.Cpu.MaxStatePct = (int)e.NewValue; }

    void BuildGuide()
    {
        var g = Vm.Cpu.Guide;
        Guide.Children.Clear();
        Guide.Children.Add(new TextBlock { Text = "BIOS tuning guide: " + g.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 16, Foreground = ThemeBrushes.Text, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap });
        Guide.Children.Add(new TextBlock { Text = g.Summary, Foreground = ThemeBrushes.Muted, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap });
        var i = 1;
        foreach (var s in g.Steps) Guide.Children.Add(new TextBlock { Text = $"{i++}. {s}", Foreground = ThemeBrushes.Text, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap });
        foreach (var w in g.Warnings) Guide.Children.Add(new TextBlock { Text = "Note: " + w, Foreground = ThemeBrushes.Muted, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap });
        Guide.Children.Add(new TextBlock { Text = "Redline never changes BIOS settings itself.", Foreground = ThemeBrushes.Muted, FontSize = 12 });
    }
}
