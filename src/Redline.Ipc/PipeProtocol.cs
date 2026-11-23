using System.Text;
using System.Text.Json;
using Redline.Core.Storage;

namespace Redline.Ipc;

public sealed class IpcProtocolException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Newline-delimited JSON over the helper pipe.</summary>
public static class PipeProtocol
{
    public const string PipeName = "Redline.Helper.v1";
    public const int MaxLineBytes = 1024 * 1024;

    public static JsonSerializerOptions Options { get; } = new(Json.Options) { WriteIndented = false };

    public static string Serialize(IpcMessage m) => JsonSerializer.Serialize(m, Options);

    public static IpcMessage Deserialize(string line)
    {
        if (Encoding.UTF8.GetByteCount(line) > MaxLineBytes) throw new IpcProtocolException("Message too large.");
        IpcMessage? m;
        try { m = JsonSerializer.Deserialize<IpcMessage>(line, Options); }
        catch (JsonException e) { throw new IpcProtocolException("Malformed message.", e); }
        if (m is null || string.IsNullOrEmpty(m.Type)) throw new IpcProtocolException("Malformed message.");
        if (!MessageTypes.All.Contains(m.Type)) throw new IpcProtocolException($"Unknown message type '{m.Type}'.");
        return m;
    }
}
