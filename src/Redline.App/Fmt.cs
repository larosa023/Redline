using Microsoft.UI.Xaml;

namespace Redline.App;

/// <summary>Formatting helpers for x:Bind function bindings.</summary>
public static class Fmt
{
    public static string Mhz(double v) => v <= 0 ? "—" : $"{v:N0}";
    public static string Ghz(double? v) => v is > 0 ? $"{v / 1000:0.0} GHz" : "—";
    public static string Deg(double v) => v <= 0 ? "—" : $"{v:0}°";
    public static string DegN(double? v) => v is > 0 ? $"{v:0}°" : "—";
    public static string Watts(double v) => v <= 0 ? "—" : $"{v:0} W";
    public static string Pct(double v) => $"{v:0}%";
    public static string Busy(double v) => $"GPU {v:0}% busy";
    public static string PctN(double? v) => v is { } x ? $"{x:0}% busy" : "";
    public static Visibility Vis(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility NotVis(bool b) => b ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility VisText(string? s) => string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible;
    public static double Progress(double f) => Math.Clamp(f * 100, 0, 100);
    public static string Hours(TimeSpan x) => $"{(int)x.TotalHours}h {x.Minutes:00}m";
    public static string Kwh(double x) => $"{x:0.0} kWh";
    public static string Money(decimal x, string sym) => $"{sym}{x:0.00}";
    public static string Int(int v) => v.ToString();
}
