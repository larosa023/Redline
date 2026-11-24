using Redline.Ipc;

namespace Redline.App.ViewModels;

/// <summary>In-game overlay text. Visible only while it's on and a game has presented frames in the last few seconds.</summary>
public sealed class OverlayViewModel(ShellViewModel shell) : ObservableObject
{
    DateTimeOffset _last = DateTimeOffset.MinValue;
    string _fps = "", _stats = "";
    bool _visible;

    public bool Enabled => shell.State?.Settings.OverlayEnabled == true;
    public string Corner => shell.State?.Settings.OverlayCorner ?? "TopLeft";
    public bool Visible { get => _visible; private set => SetProperty(ref _visible, value); }
    public string FpsText { get => _fps; private set => SetProperty(ref _fps, value); }
    public string StatsText { get => _stats; private set => SetProperty(ref _stats, value); }

    internal void OnFps(FpsSampleEvent e)
    {
        _last = DateTimeOffset.UtcNow;
        FpsText = $"{e.AvgFps:0} FPS · 1% low {e.OnePercentLowFps:0}";
        Tick(_last);
    }

    internal void OnTelemetry(TelemetryEvent e)
    {
        if (e.Gpu is { } g)
            StatsText = $"GPU {g.TempC:0}° · {g.UsagePct:0}% · {g.PowerW:0} W" + (e.Cpu?.PackageTempC is { } c ? $"   CPU {c:0}° · {e.Cpu.UsagePct:0}%" : "");
    }

    public void Tick(DateTimeOffset now) => Visible = Enabled && now - _last < TimeSpan.FromSeconds(3);

    internal void OnState() { OnPropertyChanged(nameof(Enabled)); OnPropertyChanged(nameof(Corner)); Tick(DateTimeOffset.UtcNow); }
}
