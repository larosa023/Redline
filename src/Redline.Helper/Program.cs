using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Redline.Helper;
using Redline.Ipc;

[assembly: SupportedOSPlatform("windows")]

if (args.Length > 0 && args[0] == "--install") return Installer.Install(args.Length > 1 ? args[1] : AppContext.BaseDirectory);
if (args.Length > 0 && args[0] == "--uninstall") return Installer.Uninstall(purge: args.Contains("--purge"));

var demo = args.Contains("--demo");
var pipe = args.SkipWhile(a => a != "--pipe").Skip(1).FirstOrDefault() ?? PipeProtocol.PipeName;
var dataDir = demo ? Path.Combine(Path.GetTempPath(), "RedlineDemo") : InstallLayout.DataDir;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = InstallLayout.ServiceName);
builder.Services.AddHostedService(_ => new HelperService(dataDir, demo, pipe));
await builder.Build().RunAsync();
return 0;

[SupportedOSPlatform("windows")]
static class Installer
{
    /// <summary>Copies the helper into an admin-protected folder and (re)registers the service. Requires elevation.</summary>
    public static int Install(string sourceDir)
    {
        var version = Composition.Version;
        var target = InstallLayout.HelperDir(version);
        Directory.CreateDirectory(target);
        foreach (var f in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, f);
            var dst = Path.Combine(target, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(f, dst, overwrite: true);
        }
        new DirectoryInfo(Path.GetDirectoryName(target)!).SetAccessControl(InstallLayout.ProtectedFolder());
        new DirectoryInfo(target).SetAccessControl(InstallLayout.ProtectedFolder());
        Directory.CreateDirectory(InstallLayout.DataDir);
        new DirectoryInfo(InstallLayout.DataDir).SetAccessControl(InstallLayout.DataFolder());

        var exe = Path.Combine(target, "Redline.Helper.exe");
        Sc("stop", InstallLayout.ServiceName);
        var exists = Sc("query", InstallLayout.ServiceName) == 0;
        var bin = $"binPath= \"\\\"{exe}\\\"\"";
        var r = exists
            ? Sc("config", $"{InstallLayout.ServiceName} {bin} start= auto")
            : Sc("create", $"{InstallLayout.ServiceName} {bin} start= auto DisplayName= \"Redline Helper\"");
        if (r != 0) return r;
        Sc("description", $"{InstallLayout.ServiceName} \"Applies Redline GPU tunes and enforces its safety rules.\"");
        Sc("failure", $"{InstallLayout.ServiceName} reset= 86400 actions= restart/5000/restart/5000//");
        r = Sc("start", InstallLayout.ServiceName);
        foreach (var old in Directory.EnumerateDirectories(Path.GetDirectoryName(target)!).Where(d => !d.Equals(target, StringComparison.OrdinalIgnoreCase)))
            try { Directory.Delete(old, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return r is 0 or 1056 ? 0 : r;   // 1056 = already running
    }

    public static int Uninstall(bool purge)
    {
        Sc("stop", InstallLayout.ServiceName);
        Thread.Sleep(2000);
        Sc("delete", InstallLayout.ServiceName);
        try { Directory.Delete(Path.GetDirectoryName(InstallLayout.HelperDir("x"))!, true); } catch (Exception) { }
        if (purge) try { Directory.Delete(InstallLayout.DataDir, true); } catch (Exception) { }
        return 0;
    }

    static int Sc(string verb, string rest)
    {
        using var p = Process.Start(new ProcessStartInfo("sc.exe", $"{verb} {rest}") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
        p.WaitForExit();
        return p.ExitCode;
    }
}
