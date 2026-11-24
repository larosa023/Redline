using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Redline.Core.Diagnostics;

/// <summary>Names to strip from a report: Windows user names and the PC's name.</summary>
public sealed record ReportRedaction(IReadOnlyList<string> UserNames, IReadOnlyList<string> MachineNames)
{
    public static ReportRedaction ForCurrentUser() => new([Environment.UserName], [Environment.MachineName]);
}

/// <summary>
/// Builds the "Send report" zip: the helper's files, the app's recent logs and a README. Nothing is uploaded;
/// the user decides where the file goes. User names, user-folder paths and the PC name are replaced.
/// </summary>
public static partial class ReportBuilder
{
    public const int MaxFileChars = 512 * 1024;
    static readonly TimeSpan LogAge = TimeSpan.FromDays(7);

    public static void BuildZip(string outPath, IReadOnlyDictionary<string, string> helperFiles, IEnumerable<string> appLogDirs,
        ReportRedaction redaction, DateTimeOffset now)
    {
        var tmp = outPath + ".tmp";
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            Add(zip, "README.txt", Readme(now), redaction);
            foreach (var (name, text) in helperFiles.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                Add(zip, "helper/" + SafeName(name), text, redaction);
            foreach (var dir in appLogDirs.Where(Directory.Exists))
                foreach (var file in Directory.EnumerateFiles(dir, "*.log").Concat(Directory.EnumerateFiles(dir, "crash-*.txt")).Order(StringComparer.Ordinal))
                {
                    if (now.UtcDateTime - File.GetLastWriteTimeUtc(file) > LogAge) continue;
                    string text;
                    try
                    {
                        using var s = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        text = new StreamReader(s).ReadToEnd();
                    }
                    catch (IOException) { continue; }
                    Add(zip, "app/" + SafeName(Path.GetFileName(file)), text, redaction);
                }
        }
        File.Move(tmp, outPath, overwrite: true);
    }

    static void Add(ZipArchive zip, string entry, string text, ReportRedaction r)
    {
        if (text.Length > MaxFileChars) text = "[earlier lines trimmed]\n" + text[^MaxFileChars..];
        var e = zip.CreateEntry(entry, CompressionLevel.Optimal);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
        w.Write(Redact(text, r));
    }

    /// <summary>Only a plain file name survives: no folders, no "..".</summary>
    static string SafeName(string name)
    {
        var leaf = name.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "file";
        leaf = leaf.Replace("..", "_");
        return leaf.Length == 0 ? "file" : leaf;
    }

    public static string Redact(string text, ReportRedaction r)
    {
        text = UserFolder().Replace(text, m => m.Groups[1].Value + "<user>");
        foreach (var u in r.UserNames.Where(n => n.Length >= 2)) text = Regex.Replace(text, Regex.Escape(u), "<user>", RegexOptions.IgnoreCase);
        foreach (var m in r.MachineNames.Where(n => n.Length >= 2)) text = Regex.Replace(text, Regex.Escape(m), "<pc>", RegexOptions.IgnoreCase);
        return text;
    }

    // C:\Users\name or C:\\Users\\name (JSON-escaped) → keep the prefix, replace the name.
    [GeneratedRegex(@"(?i)(\\{1,2}Users\\{1,2})(?!<user>)[^\\/""\r\n]+")]
    private static partial Regex UserFolder();

    static string Readme(DateTimeOffset now) => $"""
        Redline diagnostics report — created {now:yyyy-MM-dd HH:mm zzz}

        This file stays on your PC until you choose to share it.
        It contains: your hardware summary, Redline's settings, the safety journal, the helper's
        install log, and the last 7 days of Redline's own logs.
        Your Windows user name, user-folder paths and PC name have been replaced with <user> and <pc>.
        It does not contain anything from your games, browsers or documents.

        """;
}
