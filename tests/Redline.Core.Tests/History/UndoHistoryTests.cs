using Redline.Core.Gpu;
using Redline.Core.History;
using Xunit;

namespace Redline.Core.Tests.History;

public class UndoHistoryTests
{
    static HistoryEntry E(string dev, int core) =>
        new(DateTimeOffset.UnixEpoch, dev, TuneSettings.Factory, TuneSettings.Factory with { CoreOffsetMhz = core }, "test");

    [Fact]
    public void Undo_ReturnsLastEntryForDevice()
    {
        var h = new UndoHistory();
        h.Push(E("a", 15)); h.Push(E("a", 30));
        Assert.True(h.CanUndo("a"));
        Assert.Equal(30, h.Undo("a")!.After.CoreOffsetMhz);
        Assert.Equal(15, h.Undo("a")!.After.CoreOffsetMhz);
        Assert.False(h.CanUndo("a"));
        Assert.Null(h.Undo("a"));
    }

    [Fact]
    public void Undo_IsPerDevice()
    {
        var h = new UndoHistory();
        h.Push(E("a", 15)); h.Push(E("b", 45));
        Assert.Equal(15, h.Undo("a")!.After.CoreOffsetMhz);
        Assert.True(h.CanUndo("b"));
        Assert.Single(h.Recent("b"));
    }

    [Fact]
    public void Capacity_DropsOldest()
    {
        var h = new UndoHistory();
        for (var i = 0; i < 51; i++) h.Push(E("a", i));
        var recent = h.Recent("a");
        Assert.Equal(50, recent.Count);
        Assert.Equal(50, recent[0].After.CoreOffsetMhz); // newest first
        Assert.Equal(1, recent[^1].After.CoreOffsetMhz);
    }
}
