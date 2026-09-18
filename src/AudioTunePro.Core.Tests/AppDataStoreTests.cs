using AudioTunePro.Core.Models;
using AudioTunePro.Core.Services;
using Xunit;

namespace AudioTunePro.Core.Tests;

public class AppDataStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppDataStore _store;

    public AppDataStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AudioTuneProTests_" + Guid.NewGuid());
        _store = new AppDataStore(_tempDir);
    }

    [Fact]
    public void Settings_round_trip_preserves_values()
    {
        var settings = new AppSettings
        {
            ActivePresetName = "Rock",
            StartWithWindows = true,
            StartMinimizedToTray = false,
            ShowLevelMeter = false,
        };

        _store.SaveSettings(settings);
        var loaded = _store.LoadSettings();

        Assert.Equal("Rock", loaded.ActivePresetName);
        Assert.True(loaded.StartWithWindows);
        Assert.False(loaded.StartMinimizedToTray);
        Assert.False(loaded.ShowLevelMeter);
    }

    [Fact]
    public void Missing_settings_file_returns_defaults()
    {
        var loaded = _store.LoadSettings();
        Assert.Equal(new AppSettings().ActivePresetName, loaded.ActivePresetName);
    }

    [Fact]
    public void User_presets_round_trip_preserves_band_gains_and_required_members()
    {
        var engine = new EqEngine { BassDb = 4.5, TrebleDb = -2, PreampDb = -1 };
        engine.Bands[0].GainDb = 3.3;
        engine.Bands[9].GainDb = -6;

        var preset = new Preset { Name = "My Custom Preset", Description = "test", Engine = engine };

        _store.SaveUserPresets(new[] { preset });
        var loaded = _store.LoadUserPresets();

        var reloaded = Assert.Single(loaded);
        Assert.Equal("My Custom Preset", reloaded.Name);
        Assert.Equal(4.5, reloaded.Engine.BassDb);
        Assert.Equal(3.3, reloaded.Engine.Bands[0].GainDb);
        Assert.Equal(-6, reloaded.Engine.Bands[9].GainDb);
        Assert.Equal(31, reloaded.Engine.Bands[0].FrequencyHz);
    }

    [Fact]
    public void Built_in_presets_are_never_persisted()
    {
        var builtIn = new Preset { Name = "Flat", Engine = new EqEngine(), IsBuiltIn = true };
        var custom = new Preset { Name = "Custom", Engine = new EqEngine(), IsBuiltIn = false };

        _store.SaveUserPresets(new[] { builtIn, custom });
        var loaded = _store.LoadUserPresets();

        Assert.Single(loaded);
        Assert.Equal("Custom", loaded[0].Name);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }
}
