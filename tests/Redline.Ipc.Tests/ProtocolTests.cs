using Redline.Core.Gpu;
using Redline.Core.Safety;
using Redline.Ipc;
using Xunit;

namespace Redline.Ipc.Tests;

public class ProtocolTests
{
    [Fact]
    public void EveryRequestType_RoundTrips()
    {
        Assert.True(MessageTypes.Requests.Count >= 18);
        var id = 1;
        foreach (var t in MessageTypes.Requests.Concat(MessageTypes.Events))
        {
            var m = new IpcMessage(id++, t, null);
            var back = PipeProtocol.Deserialize(PipeProtocol.Serialize(m));
            Assert.Equal(m.Id, back.Id);
            Assert.Equal(t, back.Type);
        }
    }

    [Fact]
    public void TypedPayload_RoundTrips()
    {
        var req = new StageApplyRequest("gpu0", new TuneSettings(90, 150, 1000, null, null), SafetyOptions.Default);
        var m = IpcMessage.Create(7, MessageTypes.StageApply, req);
        var line = PipeProtocol.Serialize(m);
        Assert.DoesNotContain("\n", line);
        Assert.Equal(req, PipeProtocol.Deserialize(line).PayloadAs<StageApplyRequest>());
    }

    [Fact]
    public void UnknownType_Throws_IpcProtocolException() =>
        Assert.Throws<IpcProtocolException>(() => PipeProtocol.Deserialize("""{"id":1,"type":"FormatDisk","payload":null}"""));

    [Fact]
    public void Garbage_Throws_IpcProtocolException() =>
        Assert.Throws<IpcProtocolException>(() => PipeProtocol.Deserialize("not json at all"));

    [Fact]
    public void OversizedLine_Rejected() =>
        Assert.Throws<IpcProtocolException>(() => PipeProtocol.Deserialize(new string('x', PipeProtocol.MaxLineBytes + 1)));

    [Fact]
    public void Error_Response_CarriesMessage()
    {
        var e = IpcMessage.Error(3, "nope");
        var back = PipeProtocol.Deserialize(PipeProtocol.Serialize(e));
        Assert.Equal(MessageTypes.ErrorResponse, back.Type);
        Assert.Equal("nope", back.PayloadAs<ErrorPayload>()!.Message);
    }
}
