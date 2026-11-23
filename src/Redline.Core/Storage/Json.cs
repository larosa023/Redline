using System.Text.Json;
using System.Text.Json.Serialization;

namespace Redline.Core.Storage;

public static class Json
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Writes a file so that it is either fully old or fully new, flushed to disk.</summary>
    public static void WriteAtomic(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        using (var w = new StreamWriter(fs))
        {
            w.Write(contents);
            w.Flush();
            fs.Flush(true);
        }
        File.Move(tmp, path, overwrite: true);
    }
}
