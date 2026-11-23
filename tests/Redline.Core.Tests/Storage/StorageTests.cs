using Redline.Core.Gpu;
using Redline.Core.Storage;
using Redline.Core.Tests.Fakes;
using Xunit;

namespace Redline.Core.Tests.Storage;

public class StorageTests : IDisposable
{
    readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    JsonStore<AppSettings> Settings() => new(_dir.File("settings.json"), () => new AppSettings());

    [Fact]
    public void Load_Missing_ReturnsDefaults()
    {
        var s = Settings().Load();
        Assert.Equal("daylight", s.Theme);
        Assert.False(s.StartWithWindows);
        Assert.Equal(83, s.TempCeilingC);
    }

    [Fact]
    public void Save_Then_Load_RoundTrips()
    {
        var store = Settings();
        store.Save(new AppSettings { Theme = "neon", ElectricityRatePerKwh = 0.31m, StartWithWindows = true, StartupProfileId = "whisper" });
        var s = Settings().Load();
        Assert.Equal("neon", s.Theme);
        Assert.Equal(0.31m, s.ElectricityRatePerKwh);
        Assert.Equal("whisper", s.StartupProfileId);
    }

    [Fact]
    public void Load_CorruptJson_BacksUpAndUsesDefaults()
    {
        File.WriteAllText(_dir.File("settings.json"), "{not json");
        var s = Settings().Load();
        Assert.Equal("daylight", s.Theme);
        Assert.True(File.Exists(_dir.File("settings.json.corrupt")));
    }

    [Fact]
    public void Load_NewerSchemaVersion_UsesDefaultsAndBacksUp()
    {
        File.WriteAllText(_dir.File("settings.json"), """{ "schemaVersion": 99, "theme": "neon" }""");
        Assert.Equal("daylight", Settings().Load().Theme);
        Assert.True(File.Exists(_dir.File("settings.json.corrupt")));
    }

    [Fact]
    public void Defaults_HaveSendItWhisperFactory()
    {
        var all = new ProfileStore(_dir.Path).All();
        Assert.Equal(["send-it", "whisper", "factory"], all.Select(p => p.Id).ToList());
        Assert.Equal(["Send it", "Whisper", "Factory"], all.Select(p => p.Name).ToList());
        Assert.True(all.Single(p => p.Id == "factory").Gpu.IsFactory);
    }

    [Fact]
    public void Delete_Factory_Throws() =>
        Assert.Throws<InvalidOperationException>(() => new ProfileStore(_dir.Path).Delete("factory"));

    [Fact]
    public void Rename_EmptyName_Throws() =>
        Assert.Throws<ArgumentException>(() => new ProfileStore(_dir.Path).Rename("whisper", "  "));

    [Fact]
    public void Add_GeneratesUniqueId_AndPersists()
    {
        var store = new ProfileStore(_dir.Path);
        var a = store.Add("Streaming", "broadcast");
        var b = store.Add("Streaming", "broadcast");
        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(5, new ProfileStore(_dir.Path).All().Count);
    }

    [Fact]
    public void Update_PersistsSettings()
    {
        var store = new ProfileStore(_dir.Path);
        var p = store.Get("send-it") with { Gpu = TuneSettings.Factory with { CoreOffsetMhz = 150 }, StabilityTested = true };
        store.Update(p);
        Assert.Equal(150, new ProfileStore(_dir.Path).Get("send-it").Gpu.CoreOffsetMhz);
    }

    [Fact]
    public void Update_Factory_Throws()
    {
        var store = new ProfileStore(_dir.Path);
        Assert.Throws<InvalidOperationException>(() => store.Update(store.Get("factory") with { Gpu = TuneSettings.Factory with { CoreOffsetMhz = 15 } }));
    }

    [Fact]
    public void Profiles_CorruptFile_DefaultsAndBackup()
    {
        File.WriteAllText(Path.Combine(_dir.Path, "profiles.json"), "[[[");
        Assert.Equal(3, new ProfileStore(_dir.Path).All().Count);
        Assert.True(File.Exists(Path.Combine(_dir.Path, "profiles.json.corrupt")));
    }
}
