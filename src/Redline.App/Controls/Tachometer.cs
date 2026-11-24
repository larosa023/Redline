using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Redline.App.Theming;
using Windows.Foundation;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace Redline.App.Controls;

/// <summary>Car-style gauge: track, value arc, marked RED zone and a gently swaying needle.</summary>
public sealed class Tachometer : Canvas
{
    const double W = 300, H = 190, Cx = 150, Cy = 150, R = 120;
    readonly Path _track = Arc(0, 1, R), _value = Arc(0, 0, R), _red = Arc(0.85, 1, R), _redRing = Arc(0.85, 1, R + 16);
    readonly Line _needle = new() { StrokeThickness = 4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    readonly Ellipse _hub = new() { Width = 18, Height = 18 }, _hubInner = new() { Width = 8, Height = 8 };
    readonly TextBlock _redLabel = new() { Text = "RED", FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.Bold, CharacterSpacing = 100 };
    double _fraction, _redFrom = 0.85;

    public Tachometer()
    {
        Width = W; Height = H;
        _track.StrokeThickness = 18; _value.StrokeThickness = 18; _red.StrokeThickness = 18; _redRing.StrokeThickness = 3;
        _track.StrokeStartLineCap = _track.StrokeEndLineCap = PenLineCap.Round;
        _value.StrokeStartLineCap = _value.StrokeEndLineCap = PenLineCap.Round;
        Children.Add(_track); Children.Add(_value); Children.Add(_red); Children.Add(_redRing); Children.Add(_redLabel);
        Children.Add(_needle); Children.Add(_hub); Children.Add(_hubInner);
        SetLeft(_hub, Cx - 9); SetTop(_hub, Cy - 9); SetLeft(_hubInner, Cx - 4); SetTop(_hubInner, Cy - 4);
        var p = Point(0.93, R + 30); SetLeft(_redLabel, p.X - 4); SetTop(_redLabel, p.Y - 8);
        AutomationProperties.SetName(this, "Tachometer");
        ThemeBrushes.Changed += Recolor;
        Recolor();
        Update(0, 0.85);
    }

    void Recolor()
    {
        _track.Stroke = ThemeBrushes.Track; _value.Stroke = ThemeBrushes.Text; _red.Stroke = ThemeBrushes.Accent; _redRing.Stroke = ThemeBrushes.Accent;
        _redLabel.Foreground = ThemeBrushes.Accent; _needle.Stroke = ThemeBrushes.Accent; _hub.Fill = ThemeBrushes.Accent; _hubInner.Fill = ThemeBrushes.Surface;
    }

    public void Update(double fraction, double redlineFrom)
    {
        _fraction = Math.Clamp(fraction, 0, 1); _redFrom = redlineFrom;
        _value.Data = ArcGeometry(0, Math.Min(_fraction, _redFrom), R);
        _red.Data = ArcGeometry(_redFrom, 1, R); _redRing.Data = ArcGeometry(_redFrom, 1, R + 16);
        var tip = Point(_fraction, R - 20);
        _needle.X1 = Cx; _needle.Y1 = Cy; _needle.X2 = tip.X; _needle.Y2 = tip.Y;
    }

    static Point Point(double f, double r)
    {
        var a = Math.PI * (1 - f);                       // 180° at f=0 → 0° at f=1
        return new Point(Cx + r * Math.Cos(a), Cy - r * Math.Sin(a));
    }

    static Path Arc(double from, double to, double r) => new() { Data = ArcGeometry(from, to, r), Fill = null };

    static Geometry ArcGeometry(double from, double to, double r)
    {
        if (to - from < 0.0005) to = from + 0.0005;
        var start = Point(from, r);
        var end = Point(to, r);
        var fig = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        fig.Segments.Add(new ArcSegment { Point = end, Size = new Size(r, r), SweepDirection = SweepDirection.Clockwise, IsLargeArc = false });
        var g = new PathGeometry();
        g.Figures.Add(fig);
        return g;
    }
}
