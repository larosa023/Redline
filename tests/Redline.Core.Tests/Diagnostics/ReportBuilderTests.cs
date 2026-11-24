using System.IO.Compression;
using Redline.Core.Diagnostics;
using Redline.Core.Tests.Fakes;
using Xunit;

namespace Redline.Core.Tests.Diagnostics;

public class ReportBuilderTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 23, 22, 0, 0, TimeSpan.FromHours(-5));

    static Dictionary<string, string> Read(string zip)
    {
        using var z = ZipFile.OpenRead(zip);
        return z.Entries.ToDictionary(e => e.FullName, e => new StreamReader(e.Open()).ReadToEnd());
    }

    [Fact]
    public void Zip_ContainsExpectedEntries()
    {
        using var dir = new TempDir();
        var logs = Directory.CreateDirectory(Path.Combine(dir.Path, "logs")).FullName;
        File.WriteAllText(Path.Combine(logs, "app.log"), "started");
        File.WriteAllText(Path.Combine(logs, "crash-20261123-210000.txt"), "boom");
        File.WriteAllText(Path.Combine(logs, "notes.docx"), "private");
        var old = Path.Combine(logs, "old.log");
        File.WriteAllText(old, "ancient");
        File.SetLastWriteTimeUtc(old, Now.UtcDateTime.AddDays(-10));
        var helper = new Dictionary<string, string> { ["system.json"] = "{}", ["journal.json"] = "{}", ["settings.json"] = "{}", ["install.log"] = "done" };
        var zip = Path.Combine(dir.Path, "report.zip");

        ReportBuilder.BuildZip(zip, helper, [logs], new ReportRedaction([], []), Now);

        var entries = Read(zip);
        Assert.Contains("README.txt", entries.Keys);
        foreach (var k in helper.Keys) Assert.Contains($"helper/{k}", entries.Keys);
        Assert.Equal("started", entries["app/app.log"]);
        Assert.DoesNotContain("app/old.log", entries.Keys);
        Assert.Equal("boom", entries["app/crash-20261123-210000.txt"]);
        Assert.DoesNotContain("app/notes.docx", entries.Keys);
    }

    [Fact]
    public void UserPaths_Redacted()
    {
        using var dir = new TempDir();
        var helper = new Dictionary<string, string>
        {
            ["install.log"] = @"install from C:\Users\Anthony\AppData\Local\Redline\current\helper on ANTHONY-PC",
            ["settings.json"] = """{"path":"C:\\Users\\Anthony\\Documents"}""",
        };
        var zip = Path.Combine(dir.Path, "r.zip");

        ReportBuilder.BuildZip(zip, helper, [], new ReportRedaction(["Anthony"], ["ANTHONY-PC"]), Now);

        var all = string.Concat(Read(zip).Values);
        Assert.DoesNotContain("Anthony", all);
        Assert.DoesNotContain("ANTHONY-PC", all);
        Assert.Contains(@"C:\Users\<user>\AppData", all);
        Assert.Contains(@"C:\\Users\\<user>\\Documents", all);
        Assert.Contains("<pc>", all);
    }

    [Fact]
    public void ShortUserName_OnlyWholeWordsReplaced()
    {
        var text = ReportBuilder.Redact("Al started the install on AL-PC", new ReportRedaction(["Al"], ["AL-PC"]));
        Assert.Equal("<user> started the install on <pc>", text);
    }

    [Fact]
    public void BigLog_KeepsOnlyTheEnd()
    {
        using var dir = new TempDir();
        var big = new string('a', ReportBuilder.MaxFileChars) + "THE-END";
        var zip = Path.Combine(dir.Path, "r.zip");

        ReportBuilder.BuildZip(zip, new Dictionary<string, string> { ["install.log"] = big }, [], new ReportRedaction([], []), Now);

        var text = Read(zip)["helper/install.log"];
        Assert.EndsWith("THE-END", text);
        Assert.True(text.Length <= ReportBuilder.MaxFileChars + 100);
    }

    [Fact]
    public void FileNames_FromHelper_CannotEscapeTheZipFolder()
    {
        using var dir = new TempDir();
        var zip = Path.Combine(dir.Path, "r.zip");
        ReportBuilder.BuildZip(zip, new Dictionary<string, string> { [@"..\..\evil.txt"] = "x", ["../up.txt"] = "y" }, [], new ReportRedaction([], []), Now);
        Assert.All(Read(zip).Keys, k => Assert.DoesNotContain("..", k));
    }
}
