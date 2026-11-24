using System.Text.Json;
using Redline.Core.Cpu;
using Redline.Core.Games;
using Redline.Core.Gpu;
using Redline.Core.Safety;
using Redline.Core.Stats;

namespace Redline.Core.Storage;

public interface IVersioned { int SchemaVersion { get; } }

/// <summary>JSON file with atomic saves. Corrupt or newer-version files are kept as *.corrupt and defaults are used.</summary>
public sealed class JsonStore<T>(string path, Func<T> defaults, int currentSchema = 1) where T : IVersioned
{
    public T Load()
    {
        if (!File.Exists(path)) return defaults();
        try
        {
            var v = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json.Options);
            if (v is not null && v.SchemaVersion <= currentSchema) return v;
        }
        catch (JsonException) { }
        File.Copy(path, path + ".corrupt", overwrite: true);
        return defaults();
    }

    public void Save(T value) => Json.WriteAtomic(path, JsonSerializer.Serialize(value, Json.Options));
}

public sealed record AppSettings : IVersioned
{
    public int SchemaVersion { get; init; } = 1;
    public string Theme { get; init; } = "daylight";
    public bool StartWithWindows { get; init; }
    public string? StartupProfileId { get; init; }
    public decimal ElectricityRatePerKwh { get; init; } = 0.17m;
    public string CurrencySymbol { get; init; } = "$";
    public int TempCeilingC { get; init; } = 83;
    public IReadOnlyDictionary<string, string> Hotkeys { get; init; } = new Dictionary<string, string>();
    public bool OverlayEnabled { get; init; }
    public string OverlayCorner { get; init; } = "TopLeft";
    public IReadOnlyList<GameRule> GameRules { get; init; } = [];
    public SafetyOptions Safety { get; init; } = SafetyOptions.Default;
    public string BaseProfileId { get; init; } = "factory";
    public double? PersonalBestMhz { get; init; }
    /// <summary>GPUs whose vendor overclocking waiver (Intel) the user accepted; re-sent each time the helper starts.</summary>
    public IReadOnlyList<string> AcceptedWaivers { get; init; } = [];
}

public sealed record Profile(string Id, string Name, string Icon, TuneSettings Gpu, CpuBoostSettings? Cpu,
    bool StabilityTested, DateTimeOffset? TestedAt, BeforeAfter? Measured)
{
    public const string SendItId = "send-it", WhisperId = "whisper", FactoryId = "factory";
}

/// <summary>Named profiles. "Factory" always exists and can't be changed or deleted.</summary>
public sealed class ProfileStore
{
    sealed record Doc(int SchemaVersion, List<Profile> Profiles) : IVersioned;

    readonly JsonStore<Doc> _store;
    readonly object _gate = new();
    List<Profile> _profiles;

    public ProfileStore(string dir)
    {
        _store = new JsonStore<Doc>(Path.Combine(dir, "profiles.json"), () => new Doc(1, Defaults()));
        _profiles = _store.Load().Profiles;
        if (_profiles.All(p => p.Id != Profile.FactoryId)) _profiles.Add(Defaults()[2]);
    }

    public static List<Profile> Defaults() =>
    [
        new(Profile.SendItId, "Send it", "bolt", TuneSettings.Factory, CpuBoostSettings.SendIt, false, null, null),
        new(Profile.WhisperId, "Whisper", "moon", TuneSettings.Factory, CpuBoostSettings.Whisper, false, null, null),
        new(Profile.FactoryId, "Factory", "home", TuneSettings.Factory, null, true, null, null),
    ];

    public IReadOnlyList<Profile> All() { lock (_gate) return _profiles.ToList(); }

    public Profile Get(string id)
    {
        lock (_gate) return _profiles.FirstOrDefault(p => p.Id == id) ?? throw new KeyNotFoundException($"No profile '{id}'.");
    }

    public Profile Add(string name, string icon)
    {
        CheckName(name);
        lock (_gate)
        {
            var baseId = new string(name.Trim().ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
            if (baseId.Length == 0) baseId = "profile";
            var id = baseId;
            for (var i = 2; _profiles.Any(p => p.Id == id); i++) id = $"{baseId}-{i}";
            var p = new Profile(id, name.Trim(), icon, TuneSettings.Factory, null, false, null, null);
            _profiles.Add(p);
            Save();
            return p;
        }
    }

    public void Update(Profile p)
    {
        lock (_gate)
        {
            if (p.Id == Profile.FactoryId) throw new InvalidOperationException("The Factory profile can't be changed.");
            var i = _profiles.FindIndex(x => x.Id == p.Id);
            if (i < 0) throw new KeyNotFoundException($"No profile '{p.Id}'.");
            CheckName(p.Name);
            _profiles[i] = p;
            Save();
        }
    }

    public void Rename(string id, string name)
    {
        CheckName(name);
        Update(Get(id) with { Name = name.Trim() });
    }

    public void Delete(string id)
    {
        lock (_gate)
        {
            if (id == Profile.FactoryId) throw new InvalidOperationException("The Factory profile can't be deleted.");
            if (_profiles.RemoveAll(p => p.Id == id) == 0) throw new KeyNotFoundException($"No profile '{id}'.");
            Save();
        }
    }

    static void CheckName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Profile name can't be empty.", nameof(name));
        if (name.Trim().Length > 40) throw new ArgumentException("Profile name is too long.", nameof(name));
    }

    void Save() => _store.Save(new Doc(1, _profiles));
}
