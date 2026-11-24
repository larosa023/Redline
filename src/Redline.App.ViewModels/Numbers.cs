namespace Redline.App.ViewModels;

public static class Numbers
{
    /// <summary>
    /// "+8", "−3", "+0". Rounds first: .NET's "+0;−0" format turns small negatives like −0.3 into "-+0".
    /// </summary>
    public static string Signed(double v, int decimals = 0)
    {
        var r = Math.Round(v, decimals, MidpointRounding.AwayFromZero);
        if (r == 0) r = 0;   // no negative zero
        var text = Math.Abs(r).ToString(decimals == 0 ? "0" : "0." + new string('0', decimals), System.Globalization.CultureInfo.CurrentCulture);
        return (r < 0 ? "−" : "+") + text;
    }
}
