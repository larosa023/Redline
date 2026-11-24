using System.Runtime.InteropServices.WindowsRuntime;
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
        ToolProfile.ItemsSource = vm.Games.Profiles;
        vm.Games.Profiles.CollectionChanged += (_, _) => ToolProfile.SelectedItem = vm.Games.Profiles.FirstOrDefault(p => p.Id == vm.Tools.SelectedProfileId);
        Tach.Update(vm.Home.TachFraction, vm.Home.TachRedlineFraction);
    }

    void OnToolProfile(object sender, SelectionChangedEventArgs e)
    {
        if (ToolProfile.SelectedItem is ProfileChoice p) Vm.Tools.SelectedProfileId = p.Id;
    }

    void OnCopyCode(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(Vm.Tools.ShareCode);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        Vm.LastError = null;
    }

    async void OnSaveScore(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            var rtb = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
            await rtb.RenderAsync(ScoreCard);
            var pixels = (await rtb.GetPixelsAsync()).ToArray();
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var enc = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
            enc.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, 96, 96, pixels);
            await enc.FlushAsync();
            var bytes = new byte[stream.Size];
            using (var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0)))
            {
                await reader.LoadAsync((uint)stream.Size);
                reader.ReadBytes(bytes);
            }
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Redline");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"chip-score-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            await File.WriteAllBytesAsync(file, bytes);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Vm.LastError = "Couldn't save the image: " + ex.Message; }
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
