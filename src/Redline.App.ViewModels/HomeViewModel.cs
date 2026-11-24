using System.Collections.ObjectModel;
using Redline.Core.Stats;
using Redline.Core.Storage;
using Redline.Ipc;

namespace Redline.App.ViewModels;

public sealed record CoreItem(string Name, string ClockText, double LoadPct);

public sealed class ProfileItemViewModel(string id, string name, string icon, string subtitle, bool active, Func<Task> apply) : ObservableObject
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Icon { get; } = icon;
    public string Subtitle { get; } = subtitle;
    public bool IsActive { get; } = active;
    public AsyncRelayCommand ApplyCommand { get; } = new(apply);
}

/// <summary>Home: tachometer, status chips, live stats, weekly recap, profiles.</summary>
public sealed class HomeViewModel(ShellViewModel shell) : ObservableObject
{
    double _core, _temp, _power, _usage, _fan, _vram;
    double? _cpuTemp, _cpuUsage, _cpuClock;
    WeeklySummary? _weekly;
    readonly Queue<double> _tempHist = new(), _powerHist = new();

    public const double TachRedline = 0.85;
    public double TachRedlineFraction => TachRedline;
    public double TachMaxMhz => Math.Max(3000, Math.Ceiling(_core / 0.78 / 100) * 100);
    public double TachFraction => TachMaxMhz <= 0 ? 0 : Math.Clamp(_core / TachMaxMhz, 0, 1);

    public bool HasGpu => shell.Device is not null;
    public string GpuName => shell.Device?.Name ?? "No graphics card found";
    public double CoreClockMhz { get => _core; private set { if (SetProperty(ref _core, value)) { OnPropertyChanged(nameof(TachFraction)); OnPropertyChanged(nameof(TachMaxMhz)); } } }
    public double TempC { get => _temp; private set => SetProperty(ref _temp, value); }
    public double PowerW { get => _power; private set => SetProperty(ref _power, value); }
    public double UsagePct { get => _usage; private set => SetProperty(ref _usage, value); }
    public double FanPct { get => _fan; private set => SetProperty(ref _fan, value); }
    public double VramUsedMb { get => _vram; private set => SetProperty(ref _vram, value); }
    public double? CpuTempC { get => _cpuTemp; private set => SetProperty(ref _cpuTemp, value); }
    public double? CpuUsagePct { get => _cpuUsage; private set => SetProperty(ref _cpuUsage, value); }
    public double? CpuClockMhz { get => _cpuClock; private set => SetProperty(ref _cpuClock, value); }
    public string CpuName => shell.State?.Cpu?.Name ?? "CPU";
    public IReadOnlyList<double> TempHistory => _tempHist.ToList();
    public IReadOnlyList<double> PowerHistory => _powerHist.ToList();

    public ObservableCollection<string> Chips { get; } = new();
    public ObservableCollection<CoreItem> Cores { get; } = new();

    string _memClock = "—", _hotspot = "—", _memTemp = "—", _vramText = "—", _rpm = "—", _plim = "—", _limit = "", _ram = "—", _cpuPower = "—";
    public string MemClockText { get => _memClock; private set => SetProperty(ref _memClock, value); }
    public string HotspotText { get => _hotspot; private set => SetProperty(ref _hotspot, value); }
    public string MemTempText { get => _memTemp; private set => SetProperty(ref _memTemp, value); }
    public string VramText { get => _vramText; private set => SetProperty(ref _vramText, value); }
    public string FanRpmText { get => _rpm; private set => SetProperty(ref _rpm, value); }
    public string PowerLimitText { get => _plim; private set => SetProperty(ref _plim, value); }
    public string LimitText { get => _limit; private set => SetProperty(ref _limit, value); }
    public string RamText { get => _ram; private set => SetProperty(ref _ram, value); }
    public string CpuPowerText { get => _cpuPower; private set => SetProperty(ref _cpuPower, value); }

    public static string LimitWords(Core.Gpu.LimitReason r)
    {
        if (r == Core.Gpu.LimitReason.None) return "Nothing is holding it back";
        var parts = new List<string>();
        if (r.HasFlag(Core.Gpu.LimitReason.Power)) parts.Add("power");
        if (r.HasFlag(Core.Gpu.LimitReason.Temperature)) parts.Add("temperature");
        if (r.HasFlag(Core.Gpu.LimitReason.Voltage)) parts.Add("voltage");
        if (r.HasFlag(Core.Gpu.LimitReason.Slowdown)) parts.Add("a hardware slowdown");
        if (parts.Count == 0) return "Idle";
        return "Limited by " + string.Join(" and ", parts);
    }
    public ObservableCollection<ProfileItemViewModel> Profiles { get; } = new();

    public WeeklySummary? Weekly { get => _weekly; private set => SetProperty(ref _weekly, value); }
    public string CurrencySymbol => shell.State?.Settings.CurrencySymbol ?? "$";

    public string Headline => shell.Device?.Active.IsFactory != false
        ? Copy.HeroLine(0, 0)
        : shell.State?.Profiles.FirstOrDefault(p => p.Id == shell.Device!.ActiveProfileId)?.Measured is { } m
            ? Copy.HeroLine(m.PerfChangePct, m.TempChangeC)
            : "Your tune is on. Run a before/after check to see what it's doing.";

    internal void OnState()
    {
        OnPropertyChanged(nameof(HasGpu)); OnPropertyChanged(nameof(GpuName)); OnPropertyChanged(nameof(Headline)); OnPropertyChanged(nameof(CpuName));
        Profiles.Clear();
        if (shell.State is not { } s || shell.Device is not { } d) return;
        foreach (var p in s.Profiles)
            Profiles.Add(new ProfileItemViewModel(p.Id, p.Name, p.Icon, Subtitle(p), d.ActiveProfileId == p.Id,
                () => shell.SendAsync(MessageTypes.ApplyProfile, new ApplyProfileRequest(d.Id, p.Id))));
        UpdateChips();
    }

    static string Subtitle(Profile p) => p.Id switch
    {
        Profile.FactoryId => "Exactly as shipped",
        _ when p.Gpu.IsFactory && !p.StabilityTested => "Not tuned yet — run auto-tune",
        _ => string.Join(" · ", new[]
        {
            p.Gpu.CoreOffsetMhz is { } c ? $"Core {c:+0;-0} MHz" : null,
            p.Gpu.MemoryOffsetMhz is { } m ? $"Memory {m:+0;-0} MHz" : null,
            p.Gpu.PowerLimitPct is { } w ? $"Power {w}%" : null,
            p.Gpu.VoltageOffsetMv is { } v ? $"Voltage {v} mV" : null,
        }.Where(x => x is not null)) is { Length: > 0 } s ? s : "CPU settings only",
    };

    internal void OnTelemetry(TelemetryEvent e)
    {
        if (e.DeviceId != shell.Device?.Id) return;
        if (e.Gpu is { } t)
        {
            CoreClockMhz = t.CoreClockMhz; TempC = t.TempC; PowerW = t.PowerW; UsagePct = t.UsagePct;
            FanPct = t.FanPct ?? 0; VramUsedMb = t.VramUsedMb;
            Push(_tempHist, t.TempC); Push(_powerHist, t.PowerW);
            MemClockText = $"{t.MemClockMhz:N0} MHz";
            HotspotText = t.HotspotC is { } h ? $"{h:0}°" : "—";
            MemTempText = t.MemTempC is { } mt ? $"{mt:0}°" : "—";
            VramText = t.VramTotalMb is { } tot ? $"{t.VramUsedMb / 1024:0.0} / {tot / 1024:0.0} GB" : $"{t.VramUsedMb / 1024:0.0} GB";
            FanRpmText = t.FanRpm is { } rpm ? $"{rpm:N0} RPM" : "—";
            PowerLimitText = t.PowerLimitUsePct is { } pl ? $"{pl:0}% of limit" : "—";
            LimitText = LimitWords(t.Limits);
            OnPropertyChanged(nameof(TempHistory)); OnPropertyChanged(nameof(PowerHistory));
        }
        if (e.Cpu is { } c)
        {
            CpuTempC = c.PackageTempC; CpuUsagePct = c.UsagePct; CpuClockMhz = c.EffectiveClockMhz;
            RamText = c.RamUsedGb is { } u && c.RamTotalGb is { } tot ? $"{u:0.0} / {tot:0.0} GB" : "—";
            CpuPowerText = c.PackagePowerW is { } w ? $"{w:0} W" : "—";
            var cores = c.CoreClocksMhz.Select((mhz, i) => new CoreItem($"Core {i + 1}", $"{mhz / 1000:0.0} GHz", i < c.CoreLoadsPct.Count ? c.CoreLoadsPct[i] : 0)).ToList();
            if (cores.Count != Cores.Count) { Cores.Clear(); foreach (var x in cores) Cores.Add(x); }
            else for (var i = 0; i < cores.Count; i++) if (Cores[i] != cores[i]) Cores[i] = cores[i];
        }
        UpdateChips();
    }

    static void Push(Queue<double> q, double v) { q.Enqueue(v); while (q.Count > 60) q.Dequeue(); }

    void UpdateChips()
    {
        var want = new List<string>();
        var ceiling = shell.State?.Settings.TempCeilingC ?? 83;
        var tuned = shell.Device is { Active.IsFactory: false };
        if (tuned && _temp > 0 && _temp < ceiling - 10) want.Add(Copy.ChipCruising);
        if (_temp >= ceiling - 5) want.Add(Copy.ChipWarm);
        if (shell.State?.NewPersonalBest == true) want.Add(Copy.ChipBest);
        if (!want.SequenceEqual(Chips)) { Chips.Clear(); foreach (var c in want) Chips.Add(c); }
    }

    internal async Task LoadWeeklyAsync()
    {
        if (await shell.SendAsync(MessageTypes.GetWeekly, null) is { } r && r.PayloadAs<WeeklyResponse>() is { Summary: { } w }) Weekly = w;
    }
}
