using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Redline.App.Theming;
using Redline.App.ViewModels.Theming;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Redline.App.Services;

/// <summary>
/// CI only (REDLINE_TOUR=&lt;folder&gt;): visits every page in a dark and a light theme and saves a JPEG of each,
/// including the parts below the fold, so every screen can be reviewed without a person clicking through.
/// </summary>
public static class ScreenTour
{
    public static async Task RunAsync(string folder, IReadOnlyList<string> pages, Action<string> navigate, ScrollViewer host, FrameworkElement root)
    {
        Directory.CreateDirectory(folder);
        await Task.Delay(4000);   // let the demo helper connect and fill the pages
        foreach (var theme in new[] { "neon", "daylight" })
        {
            ThemeBrushes.Apply(Themes.Get(theme));
            foreach (var page in pages)
            {
                navigate(page);
                await Task.Delay(1500);
                await SaveAsync(root, Path.Combine(folder, $"{theme}-{page.Replace(' ', '-')}-top.jpg"));
                if (host.ScrollableHeight > 20)
                {
                    host.ChangeView(null, host.ScrollableHeight, null, disableAnimation: true);
                    await Task.Delay(600);
                    await SaveAsync(root, Path.Combine(folder, $"{theme}-{page.Replace(' ', '-')}-bottom.jpg"));
                    host.ChangeView(null, 0, null, disableAnimation: true);
                }
            }
        }
        File.WriteAllText(Path.Combine(folder, "done.txt"), "ok");
    }

    static async Task SaveAsync(FrameworkElement root, string path)
    {
        try
        {
            var rtb = new RenderTargetBitmap();
            await rtb.RenderAsync(root);
            var pixels = (await rtb.GetPixelsAsync()).ToArray();
            using var stream = new InMemoryRandomAccessStream();
            var enc = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
            enc.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, 96, 96, pixels);
            await enc.FlushAsync();
            var bytes = new byte[stream.Size];
            stream.Seek(0);
            using (var reader = new DataReader(stream.GetInputStreamAt(0)))
            {
                await reader.LoadAsync((uint)stream.Size);
                reader.ReadBytes(bytes);
            }
            await File.WriteAllBytesAsync(path, bytes);
        }
        catch (Exception e) { File.WriteAllText(path + ".error.txt", e.ToString()); }
    }
}
