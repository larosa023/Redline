using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Redline.App.ViewModels.Theming;
using Windows.UI;

namespace Redline.App.Theming;

/// <summary>One shared brush per theme token. Switching themes recolors these brushes in place, so every element updates live.</summary>
public static class ThemeBrushes
{
    public static readonly SolidColorBrush Bg = new(), Surface = new(), Surface2 = new(), Text = new(), Muted = new(), Accent = new(),
        OnAccent = new(), Good = new(), GoodBg = new(), Line = new(), Track = new();

    public static ThemePalette Current { get; private set; } = Themes.Get("daylight");

    public static void Register(ResourceDictionary r)
    {
        r["RlBg"] = Bg; r["RlSurface"] = Surface; r["RlSurface2"] = Surface2; r["RlText"] = Text; r["RlMuted"] = Muted;
        r["RlAccent"] = Accent; r["RlOnAccent"] = OnAccent; r["RlGood"] = Good; r["RlGoodBg"] = GoodBg; r["RlLine"] = Line; r["RlTrack"] = Track;
        Apply(Current);
    }

    public static event Action? Changed;

    public static void Apply(ThemePalette p)
    {
        Current = p;
        Bg.Color = C(p.Bg); Surface.Color = C(p.Surface); Surface2.Color = C(p.Surface2); Text.Color = C(p.Text); Muted.Color = C(p.Muted);
        Accent.Color = C(p.Accent); OnAccent.Color = C(p.OnAccent); Good.Color = C(p.Good); GoodBg.Color = C(p.GoodBg);
        Line.Color = C(p.Line); Track.Color = C(p.Track);
        Changed?.Invoke();
    }

    public static Color C(string hex)
    {
        var (r, g, b) = Contrast.Parse(hex);
        return ColorHelper.FromArgb(255, r, g, b);
    }

    public static ElementTheme ElementTheme => Current.IsDark ? ElementTheme.Dark : ElementTheme.Light;
}
