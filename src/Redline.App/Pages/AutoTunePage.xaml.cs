using Microsoft.UI.Xaml.Controls;
using Redline.App.ViewModels;
using Redline.Core.AutoTune;

namespace Redline.App.Pages;

public sealed partial class AutoTunePage : UserControl
{
    public ShellViewModel Vm { get; }

    public AutoTunePage(ShellViewModel vm)
    {
        Vm = vm;
        InitializeComponent();
        ModePicker.SelectedIndex = 0;
    }

    void OnModeChanged(object sender, SelectionChangedEventArgs e)
    {
        Vm.AutoTune.Mode = ModePicker.SelectedIndex == 1 ? AutoTuneMode.Efficiency : AutoTuneMode.Performance;
        if (ModePicker.SelectedIndex == 1) Vm.AutoTune.MemoryOnly = false;   // memory-only is a Performance run
    }
}
