using Redline.App.ViewModels;
using Velopack;
using Velopack.Sources;

namespace Redline.App.Services;

/// <summary>Updates from GitHub Releases (the only network call Redline makes).</summary>
public sealed class VelopackUpdateSource : Redline.App.ViewModels.IUpdateSource
{
    const string Repo = "https://github.com/larosa023/Redline";
    readonly UpdateManager _mgr;
    UpdateInfo? _info;

    public VelopackUpdateSource()
    {
        var version = typeof(VelopackUpdateSource).Assembly.GetName().Version;
        var pre = typeof(VelopackUpdateSource).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Contains('-') == true;
        _mgr = new UpdateManager(new GithubSource(Repo, null, pre));
        _ = version;
    }

    public async Task<UpdateOffer?> CheckAsync()
    {
        if (!_mgr.IsInstalled) return null;   // running from a dev build
        _info = await _mgr.CheckForUpdatesAsync();
        if (_info is null) return null;
        var notes = _info.TargetFullRelease.NotesMarkdown ?? "";
        return new UpdateOffer(_info.TargetFullRelease.Version.ToString(), notes.Length > 600 ? notes[..600] + "…" : notes);
    }

    public async Task DownloadAndRestartAsync(UpdateOffer offer, IProgress<int> progress)
    {
        if (_info is null) return;
        await _mgr.DownloadUpdatesAsync(_info, p => progress.Report(p));
        _mgr.ApplyUpdatesAndRestart(_info.TargetFullRelease);
    }
}
