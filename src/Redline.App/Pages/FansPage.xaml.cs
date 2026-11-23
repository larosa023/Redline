using Microsoft.UI.Xaml.Controls;
using Redline.App.ViewModels;

namespace Redline.App.Pages;

public sealed partial class FansPage : UserControl
{
    public ShellViewModel Vm { get; }
    public FansPage(ShellViewModel vm) { Vm = vm; InitializeComponent(); Editor.Attach(vm.Fans); }
}
