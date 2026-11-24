using System.IO.Compression;
using Redline.App.ViewModels;
using Redline.Ipc;
using Xunit;

namespace Redline.App.ViewModels.Tests;

public class ReportTests
{
    [Fact]
    public async Task CreateReport_ZipsHelperFilesAndAppLogs()
    {
        var root = Directory.CreateTempSubdirectory("redline-report-").FullName;
        try
        {
            var logs = Directory.CreateDirectory(Path.Combine(root, "logs")).FullName;
            File.WriteAllText(Path.Combine(logs, "app.log"), "app started");
            var h = new FakeHelper
            {
                Respond = (t, _) => t == MessageTypes.ExportReport
                    ? IpcMessage.OkFor(0, new ReportResponse(new Dictionary<string, string> { ["system.json"] = "{\"gpu\":\"RTX 4090\"}" }))
                    : null,
            };
            var s = new ShellViewModel(h, FakeHelper.Inline) { Delay = HelperRecoveryTests.InstantThenNever(0) };
            await s.LoadAsync();

            var path = await s.Tools.CreateReportAsync(root, [logs]);

            Assert.NotNull(path);
            Assert.StartsWith(Path.Combine(root, "Redline-report-"), path);
            using var z = ZipFile.OpenRead(path!);
            var names = z.Entries.Select(e => e.FullName).ToList();
            Assert.Contains("helper/system.json", names);
            Assert.Contains("app/app.log", names);
            Assert.Contains("saved", s.Tools.Message);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CreateReport_HelperDown_StillSavesAppLogs()
    {
        var root = Directory.CreateTempSubdirectory("redline-report-").FullName;
        try
        {
            var s = new ShellViewModel(new FakeHelper { Available = false }, FakeHelper.Inline) { Delay = HelperRecoveryTests.InstantThenNever(0) };
            await s.LoadAsync();
            var path = await s.Tools.CreateReportAsync(root, []);
            Assert.NotNull(path);
            Assert.True(File.Exists(path));
        }
        finally { Directory.Delete(root, true); }
    }
}
