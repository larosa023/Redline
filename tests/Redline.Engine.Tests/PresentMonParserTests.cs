using Redline.Engine;
using Xunit;

namespace Redline.Engine.Tests;

public class PresentMonParserTests
{
    const string Header = "Application,ProcessID,SwapChainAddress,PresentRuntime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,MsBetweenPresents,MsInPresentAPI";

    [Fact]
    public void ParsesFrameTimes_ByProcess()
    {
        var p = new PresentMonParser();
        p.Feed(Header);
        p.Feed("Game.exe,123,0x1,DXGI,0,0,1,Hardware: Independent Flip,6.94,0.1");
        p.Feed("Game.exe,123,0x1,DXGI,0,0,1,Hardware: Independent Flip,7.10,0.1");
        p.Feed("dwm.exe,99,0x2,Other,1,0,0,Composed: Flip,16.6,0.1");
        var (proc, ft) = p.TakeBusiest()!.Value;
        Assert.Equal("Game.exe", proc);
        Assert.Equal([6.94, 7.10], ft);
        Assert.Null(p.TakeBusiest());   // drained
    }

    [Fact]
    public void FrameTimeColumn_V2_AlsoWorks()
    {
        var p = new PresentMonParser();
        p.Feed("Application,ProcessID,FrameTime,CPUBusy");
        p.Feed("Game.exe,1,8.33,2");
        Assert.Equal([8.33], p.TakeBusiest()!.Value.FrameTimesMs);
    }

    [Fact]
    public void MissingColumns_Throws_Clear() =>
        Assert.Throws<FormatException>(() => new PresentMonParser().Feed("Foo,Bar"));

    [Fact]
    public void IgnoresSystemProcesses_AndBadRows()
    {
        var p = new PresentMonParser();
        p.Feed(Header);
        p.Feed("dwm.exe,99,0x2,Other,1,0,0,Composed: Flip,16.6,0.1");
        p.Feed("garbage");
        p.Feed("Game.exe,1,0x1,DXGI,0,0,1,x,NA,0.1");
        Assert.Null(p.TakeBusiest());
    }
}
