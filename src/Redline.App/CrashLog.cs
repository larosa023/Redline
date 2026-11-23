using System.Runtime.InteropServices;

namespace Redline.App;

/// <summary>Writes startup/crash details to %LocalAppData%\Redline\logs and tells the user where they are.</summary>
public static class CrashLog
{
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Redline", "logs");

    public static void Write(string stage, Exception? e, bool show)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var file = Path.Combine(Dir, e is null ? "app.log" : $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            var text = $"[{DateTime.Now:O}] {stage} · Redline {typeof(CrashLog).Assembly.GetName().Version} · {Environment.OSVersion}\n{e}\n\n";
            File.AppendAllText(file, text);
            Console.Error.WriteLine(text);
            if (show && e is not null && Environment.GetEnvironmentVariable("REDLINE_SMOKE") != "1")
                MessageBoxW(0, $"Redline hit a problem while starting ({stage}).\n\n{e.GetType().Name}: {e.Message}\n\nDetails were saved to:\n{file}",
                    "Redline", 0x10);
        }
        catch (Exception) { }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBoxW(nint hwnd, string text, string caption, uint type);
}
