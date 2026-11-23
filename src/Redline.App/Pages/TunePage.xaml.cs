using Microsoft.UI.Xaml.Controls;
using Redline.App.ViewModels;

namespace Redline.App.Pages;

public sealed partial class TunePage : UserControl
{
    public ShellViewModel Vm { get; }
    public TunePage(ShellViewModel vm) { Vm = vm; InitializeComponent(); }
}
