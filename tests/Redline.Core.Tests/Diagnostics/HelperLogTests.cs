using Redline.Core.Diagnostics;
using Redline.Core.Tests.Fakes;
using Xunit;

namespace Redline.Core.Tests.Diagnostics;

[Collection("HelperLog")]
public class HelperLogTests
{
    [Fact]
    public void Writes_TimestampedLines_AndTimedSteps()
    {
        using var dir = new TempDir();
        var path = dir.File("helper.log");
        HelperLog.Init(path);
        HelperLog.Write("starting 0.9.0");
        using (HelperLog.Time("discover nvidia")) { }
        HelperLog.Init(null);

        var lines = File.ReadAllLines(path);
        Assert.Matches(@"^\[\d{4}-\d\d-\d\dT[^\]]+\] starting 0\.9\.0$", lines[0]);
        Assert.Contains(lines, l => System.Text.RegularExpressions.Regex.IsMatch(l, @"\] discover nvidia: \d+ ms$"));
    }

    [Fact]
    public void BigLog_IsTrimmed_OnStart()
    {
        using var dir = new TempDir();
        var path = dir.File("helper.log");
        File.WriteAllText(path, new string('x', HelperLog.MaxBytes + 10) + "\nlast line\n");
        HelperLog.Init(path);
        HelperLog.Write("new start");
        HelperLog.Init(null);
        var text = File.ReadAllText(path);
        Assert.True(text.Length < HelperLog.MaxBytes / 2 + 200);
        Assert.Contains("last line", text);
        Assert.EndsWith("new start" + Environment.NewLine, text);
    }

    [Fact]
    public void NotInitialised_DoesNothing() => HelperLog.Write("nowhere");   // must not throw
}
