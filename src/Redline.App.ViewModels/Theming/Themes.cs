using System.Globalization;

namespace Redline.App.ViewModels.Theming;

public sealed record ThemePalette(string Key, string Name, string Bg, string Surface, string Surface2, string Text, string Muted,
    string Accent, string OnAccent, string Good, string GoodBg, string Line, string Track, bool IsDark);

/// <summary>The five themes from the approved mockups (Round 2).</summary>
public static class Themes
{
    public static IReadOnlyList<ThemePalette> All { get; } =
    [
        new("daylight", "Daylight", "#F4F4F6", "#FFFFFF", "#EFEFF3", "#15151A", "#55555F", "#D92D20", "#FFFFFF", "#146B4A", "#E3F5EC", "#E2E2E8", "#ECECF0", false),
        new("midnight", "Midnight", "#0D1020", "#161A2E", "#21263F", "#EEF0FA", "#A3A8C3", "#FF4D5E", "#0D1020", "#5BE3A8", "#14352A", "#262B47", "#262B47", true),
        new("ember", "Ember", "#17120F", "#221A16", "#30251E", "#FBF1EA", "#C4B1A5", "#FF7A2F", "#17120F", "#A6E58A", "#25331C", "#3A2E26", "#382B22", true),
        new("glacier", "Glacier", "#E8F0F6", "#FFFFFF", "#E1EBF2", "#0E1B26", "#4A5D6C", "#0A6FA8", "#FFFFFF", "#12704B", "#DDF3E9", "#D3E0E9", "#DCE7EF", false),
        new("neon", "Neon", "#07070A", "#111117", "#1C1C25", "#F5F5FA", "#A0A0B2", "#FF2E88", "#07070A", "#B6FF3B", "#1F2E0A", "#24242F", "#1F1F29", true),
    ];

    public static ThemePalette Get(string? key) => All.FirstOrDefault(t => t.Key == key) ?? All[0];
}

/// <summary>WCAG 2 contrast ratio.</summary>
public static class Contrast
{
    public static double Ratio(string a, string b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    public static (byte R, byte G, byte B) Parse(string hex)
    {
        var h = hex.TrimStart('#');
        return (byte.Parse(h[..2], NumberStyles.HexNumber), byte.Parse(h[2..4], NumberStyles.HexNumber), byte.Parse(h[4..6], NumberStyles.HexNumber));
    }

    static double Luminance(string hex)
    {
        var (r, g, b) = Parse(hex);
        static double C(byte v) { var s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * C(r) + 0.7152 * C(g) + 0.0722 * C(b);
    }
}
