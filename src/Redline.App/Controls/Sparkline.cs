using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Redline.App.Controls;

/// <summary>Tiny trend line for stat cards.</summary>
public sealed class Sparkline : Canvas
{
    readonly Polyline _line = new() { StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round };

    public Sparkline()
    {
        Height = 34;
        Children.Add(_line);
        SizeChanged += (_, _) => Redraw();
    }

    IReadOnlyList<double> _values = [];
    public Brush Stroke { get => _line.Stroke; set => _line.Stroke = value; }

    public void Update(IReadOnlyList<double> values) { _values = values; Redraw(); }

    void Redraw()
    {
        _line.Points.Clear();
        if (_values.Count < 2 || ActualWidth <= 0) return;
        double min = _values.Min(), max = _values.Max();
        if (max - min < 1) { max += 0.5; min -= 0.5; }
        for (var i = 0; i < _values.Count; i++)
            _line.Points.Add(new Point(i * ActualWidth / (_values.Count - 1), Height - 3 - (_values[i] - min) / (max - min) * (Height - 6)));
    }
}
