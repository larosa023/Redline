using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Redline.App.Theming;
using Redline.App.ViewModels;
using Windows.Foundation;
using Windows.System;

namespace Redline.App.Controls;

/// <summary>Drag points to shape the fan curve. Arrow keys move the focused point (temperature ←/→, speed ↑/↓).</summary>
public sealed class FanCurveEditor : Canvas
{
    const double Pad = 36, TMin = 20, TMax = 100;
    readonly Polyline _line = new() { StrokeThickness = 3, StrokeLineJoin = PenLineJoin.Round };
    FansViewModel? _vm;
    FanPointViewModel? _drag;

    public FanCurveEditor()
    {
        Height = 280;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        SizeChanged += (_, _) => Rebuild();
        ThemeBrushes.Changed += Rebuild;
        PointerMoved += OnMove;
        PointerReleased += (_, _) => _drag = null;
        PointerExited += (_, _) => _drag = null;
    }

    public void Attach(FansViewModel vm)
    {
        _vm = vm;
        vm.Points.CollectionChanged += (_, _) => Rebuild();
        vm.PropertyChanged += (_, _) => Rebuild();
        Rebuild();
    }

    double X(double t) => Pad + (t - TMin) / (TMax - TMin) * (ActualWidth - Pad * 1.5);
    double Y(double p) => Height - Pad - p / 100 * (Height - Pad * 1.5);
    double T(double x) => TMin + (x - Pad) / (ActualWidth - Pad * 1.5) * (TMax - TMin);
    double P(double y) => (Height - Pad - y) / (Height - Pad * 1.5) * 100;

    void Rebuild()
    {
        Children.Clear();
        if (_vm is null || ActualWidth <= 0) return;
        for (var t = 20; t <= 100; t += 20)
        {
            Children.Add(new Line { X1 = X(t), X2 = X(t), Y1 = Y(0), Y2 = Y(100), Stroke = ThemeBrushes.Line, StrokeThickness = 1 });
            var lbl = new TextBlock { Text = $"{t}°", FontSize = 11, Foreground = ThemeBrushes.Muted };
            SetLeft(lbl, X(t) - 10); SetTop(lbl, Y(0) + 6); Children.Add(lbl);
        }
        for (var p = 0; p <= 100; p += 25)
        {
            Children.Add(new Line { X1 = X(TMin), X2 = X(TMax), Y1 = Y(p), Y2 = Y(p), Stroke = ThemeBrushes.Line, StrokeThickness = 1 });
            var lbl = new TextBlock { Text = $"{p}%", FontSize = 11, Foreground = ThemeBrushes.Muted };
            SetLeft(lbl, 0); SetTop(lbl, Y(p) - 8); Children.Add(lbl);
        }
        _line.Stroke = _vm.UseCustomCurve ? ThemeBrushes.Accent : ThemeBrushes.Muted;
        _line.Points.Clear();
        foreach (var pt in _vm.Points) _line.Points.Add(new Point(X(pt.TempC), Y(pt.Pct)));
        Children.Add(_line);
        foreach (var pt in _vm.Points)
        {
            var thumb = new Button
            {
                Width = 22, Height = 22, Padding = new Thickness(0), CornerRadius = new CornerRadius(11),
                Background = ThemeBrushes.Surface, BorderBrush = ThemeBrushes.Accent, BorderThickness = new Thickness(3),
                IsEnabled = _vm.UseCustomCurve,
            };
            AutomationProperties.SetName(thumb, $"Fan point {pt.TempC} degrees, {pt.Pct} percent");
            ToolTipService.SetToolTip(thumb, $"{pt.TempC} °C → {pt.Pct}%");
            var captured = pt;
            thumb.PointerPressed += (_, e) => { _drag = captured; e.Handled = true; };
            thumb.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => _drag = captured), true);
            thumb.KeyDown += (_, e) => OnKey(captured, e);
            SetLeft(thumb, X(pt.TempC) - 11); SetTop(thumb, Y(pt.Pct) - 11);
            Children.Add(thumb);
        }
    }

    void OnMove(object sender, PointerRoutedEventArgs e)
    {
        if (_drag is null || _vm is null) return;
        var pos = e.GetCurrentPoint(this).Position;
        Move(_drag, (int)Math.Round(T(pos.X)), (int)Math.Round(P(pos.Y)));
    }

    void OnKey(FanPointViewModel pt, KeyRoutedEventArgs e)
    {
        var (dt, dp) = e.Key switch { VirtualKey.Left => (-1, 0), VirtualKey.Right => (1, 0), VirtualKey.Up => (0, 1), VirtualKey.Down => (0, -1), _ => (0, 0) };
        if (dt == 0 && dp == 0) return;
        Move(pt, pt.TempC + dt, pt.Pct + dp);
        e.Handled = true;
    }

    void Move(FanPointViewModel pt, int t, int p)
    {
        var i = _vm!.Points.IndexOf(pt);
        var lo = i > 0 ? _vm.Points[i - 1].TempC + 1 : 20;
        var hi = i < _vm.Points.Count - 1 ? _vm.Points[i + 1].TempC - 1 : 100;
        pt.TempC = Math.Clamp(t, lo, hi);
        pt.Pct = Math.Clamp(p, _vm.MinPct, 100);
        Rebuild();
    }
}
