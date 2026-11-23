using Redline.App.ViewModels.Theming;
using Xunit;

namespace Redline.App.ViewModels.Tests.Theming;

public class ThemesTests
{
    [Fact]
    public void FiveThemes_InOrder() =>
        Assert.Equal(["daylight", "midnight", "ember", "glacier", "neon"], Themes.All.Select(t => t.Key).ToList());

    [Fact]
    public void Contrast_KnownValues()
    {
        Assert.Equal(21, Contrast.Ratio("#000000", "#FFFFFF"), 1);
        Assert.Equal(1, Contrast.Ratio("#777777", "#777777"), 3);
    }

    public static IEnumerable<object[]> Pairs()
    {
        foreach (var t in Themes.All)
        {
            yield return [t.Key, "text/bg", t.Text, t.Bg];
            yield return [t.Key, "text/surface", t.Text, t.Surface];
            yield return [t.Key, "muted/bg", t.Muted, t.Bg];
            yield return [t.Key, "muted/surface", t.Muted, t.Surface];
            yield return [t.Key, "muted/surface2", t.Muted, t.Surface2];
            yield return [t.Key, "onAccent/accent", t.OnAccent, t.Accent];
            yield return [t.Key, "good/goodBg", t.Good, t.GoodBg];
        }
    }

    [Fact]
    public void AllTextPairs_AtLeast4_5()
    {
        var failures = Pairs().Select(p => (Key: (string)p[0], Pair: (string)p[1], R: Contrast.Ratio((string)p[2], (string)p[3])))
                              .Where(x => x.R < 4.5).Select(x => $"{x.Key} {x.Pair} {x.R:0.00}").ToList();
        Assert.Empty(failures);
    }

    [Fact]
    public void UnknownKey_FallsBackToDaylight() => Assert.Equal("daylight", Themes.Get("vaporwave").Key);
}
