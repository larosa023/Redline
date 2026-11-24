using System.Diagnostics;

namespace Redline.Core.Diagnostics;

/// <summary>
/// The helper's own log (C:\ProgramData\Redline\helper.log): startup steps with timings and unexpected errors.
/// It ends up in "Create report". Does nothing until <see cref="Init"/> is called.
/// </summary>
public static class HelperLog
{
    public const int MaxBytes = 1024 * 1024;
    static readonly object Gate = new();
    static string? _path;

    /// <summary>Starts logging to <paramref name="path"/> (null stops). A log over 1 MB keeps only its newest half.</summary>
    public static void Init(string? path)
    {
        lock (Gate)
        {
            _path = path;
            if (path is null || !File.Exists(path)) return;
            try
            {
                var info = new FileInfo(path);
                if (info.Length <= MaxBytes) return;
                var text = File.ReadAllText(path);
                var keep = text[^(MaxBytes / 2)..];
                var nl = keep.IndexOf('\n');
                File.WriteAllText(path, nl >= 0 ? keep[(nl + 1)..] : keep);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static void Write(string message)
    {
        lock (Gate)
        {
            if (_path is null) return;
            try { File.AppendAllText(_path, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}"); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Logs "<paramref name="what"/>: N ms" when disposed.</summary>
    public static IDisposable Time(string what) => new Timer(what);

    sealed class Timer(string what) : IDisposable
    {
        readonly Stopwatch _sw = Stopwatch.StartNew();
        public void Dispose() => Write($"{what}: {_sw.ElapsedMilliseconds} ms");
    }
}
