using Redline.Core.Gpu;
using Redline.Core.Safety;
using Redline.Core.Tests.Fakes;
using Xunit;

namespace Redline.Core.Tests.Safety;

public class SafeStartJournalTests : IDisposable
{
    readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();
    static readonly TuneSettings Tuned = TuneSettings.Factory with { CoreOffsetMhz = 150 };

    SafeStartJournal New() => new(_dir.File("journal.json"));

    [Fact]
    public void Inspect_NoFile_Clean() => Assert.Equal(StartupDecision.Clean, New().Inspect().Decision);

    [Fact]
    public void Inspect_PendingStep_RecoverToFactory()
    {
        var j = New(); j.MarkStarted(); j.MarkPending("gpu0", Tuned, "auto-tune core +150");
        var (d, devices) = New().Inspect();
        Assert.Equal(StartupDecision.RecoverToFactory, d);
        Assert.Equal("auto-tune core +150", Assert.Single(devices).Reason);
    }

    [Fact]
    public void Inspect_ActiveTuneWithoutCleanShutdown_RecoverToFactory()
    {
        var j = New(); j.MarkStarted(); j.MarkPending("gpu0", Tuned, "apply"); j.MarkConfirmed("gpu0", Tuned);
        Assert.Equal(StartupDecision.RecoverToFactory, New().Inspect().Decision);
    }

    [Fact]
    public void Inspect_CleanShutdown_Clean()
    {
        var j = New(); j.MarkStarted(); j.MarkConfirmed("gpu0", Tuned); j.MarkCleanShutdown();
        Assert.Equal(StartupDecision.Clean, New().Inspect().Decision);
    }

    [Fact]
    public void Inspect_FactoryActive_UncleanShutdown_Clean()
    {
        var j = New(); j.MarkStarted(); j.MarkConfirmed("gpu0", TuneSettings.Factory);
        Assert.Equal(StartupDecision.Clean, New().Inspect().Decision);
    }

    [Fact]
    public void Inspect_CorruptFile_RecoverToFactory()
    {
        File.WriteAllText(_dir.File("journal.json"), "{garbage");
        Assert.Equal(StartupDecision.RecoverToFactory, New().Inspect().Decision);
    }

    [Fact]
    public void MarkStarted_AfterClean_ClearsCleanFlag()
    {
        var j = New(); j.MarkStarted(); j.MarkConfirmed("gpu0", Tuned); j.MarkCleanShutdown(); j.MarkStarted();
        Assert.Equal(StartupDecision.RecoverToFactory, New().Inspect().Decision);
    }
}
