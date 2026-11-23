using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Redline.App.ViewModels;

namespace Redline.App.Pages;

public sealed partial class HomePage : UserControl
{
    public ShellViewModel Vm { get; }

    public HomePage(ShellViewModel vm)
    {
        Vm = vm;
        InitializeComponent();
        vm.Home.PropertyChanged += OnHome;
        Tach.Update(vm.Home.TachFraction, vm.Home.TachRedlineFraction);
    }

    void OnHome(object? s, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(HomeViewModel.TachFraction): Tach.Update(Vm.Home.TachFraction, Vm.Home.TachRedlineFraction); break;
            case nameof(HomeViewModel.TempHistory): TempLine.Update(Vm.Home.TempHistory); break;
            case nameof(HomeViewModel.PowerHistory): PowerLine.Update(Vm.Home.PowerHistory); break;
        }
    }
}
