using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Redline.App.Controls;
using Redline.App.Theming;
using Redline.App.ViewModels;
using Redline.Core.Stats;

namespace Redline.App.Pages;

public sealed partial class StatsPage : UserControl
{
    readonly ShellViewModel _vm;
    Microsoft.UI.Dispatching.DispatcherQueueTimer? _timer;

    public StatsPage(ShellViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        vm.Stats.Series.CollectionChanged += (_, _) => Render();
        Loaded += async (_, _) =>
        {
            await vm.Stats.RefreshAsync();
            _timer = DispatcherQueue.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(5);
            _timer.Tick += async (_, _) => await vm.Stats.RefreshAsync();
            _timer.Start();
        };
        Unloaded += (_, _) => _timer?.Stop();
        Highlight();
        ThemeBrushes.Changed += () => { Render(); Highlight(); };
    }

    void OnRange(object sender, RoutedEventArgs e)
    {
        _vm.Stats.Range = (StatsRange)int.Parse((string)((Button)sender).Tag);
        Highlight();
    }

    void Highlight()
    {
        foreach (var (b, r) in new[] { (R5, StatsRange.FiveMinutes), (R60, StatsRange.Hour), (R24, StatsRange.Day) })
        {
            var on = _vm.Stats.Range == r;
            b.Background = on ? ThemeBrushes.Text : ThemeBrushes.Surface;
            b.Foreground = on ? ThemeBrushes.Bg : ThemeBrushes.Text;
        }
    }

    void Render()
    {
        Cards.Children.Clear();
        Empty.Visibility = _vm.Stats.Series.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var s in _vm.Stats.Series)
        {
            var chart = new Sparkline { Height = 90, Stroke = s.Metric.Contains("temp", StringComparison.OrdinalIgnoreCase) || s.Metric == "Hotspot" ? ThemeBrushes.Good : ThemeBrushes.Accent };
            chart.Update(s.Values);
            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(new TextBlock { Text = s.Metric, FontSize = 13, Foreground = ThemeBrushes.Muted });
            stack.Children.Add(new TextBlock { Text = s.Latest, FontSize = 26, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = ThemeBrushes.Text, FontFamily = (FontFamily)Application.Current.Resources["RlDisplay"] });
            stack.Children.Add(chart);
            stack.Children.Add(new TextBlock { Text = s.Summary, FontSize = 12, Foreground = ThemeBrushes.Muted });
            Cards.Children.Add(new Border
            {
                Background = ThemeBrushes.Surface, BorderBrush = ThemeBrushes.Line, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(22), Padding = new Thickness(18), Margin = new Thickness(0, 0, 14, 14), Child = stack,
            });
        }
    }
}
