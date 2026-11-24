using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using Redline.App.ViewModels;

namespace Redline.App.Pages;

public sealed partial class GamesPage : UserControl
{
    public ShellViewModel Vm { get; }

    public GamesPage(ShellViewModel vm)
    {
        Vm = vm;
        InitializeComponent();
        ProfilePick.ItemsSource = vm.Games.Profiles;
        vm.Games.Profiles.CollectionChanged += (_, _) => SelectProfile();
        SelectProfile();
    }

    void SelectProfile() => ProfilePick.SelectedItem = Vm.Games.Profiles.FirstOrDefault(p => p.Id == Vm.Games.NewProfileId);

    void OnProfilePicked(object sender, SelectionChangedEventArgs e)
    {
        if (ProfilePick.SelectedItem is ProfileChoice p) Vm.Games.NewProfileId = p.Id;
    }

    void OnRunningOpened(object? sender, object e)
    {
        Running.ItemsSource = Process.GetProcesses()
            .Where(p => { try { return p.MainWindowHandle != 0 && p.ProcessName != "Redline"; } catch { return false; } })
            .Select(p => p.ProcessName + ".exe").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
    }

    void OnRunningPicked(object sender, SelectionChangedEventArgs e)
    {
        if (Running.SelectedItem is string exe) Vm.Games.NewExe = exe;
    }
}
