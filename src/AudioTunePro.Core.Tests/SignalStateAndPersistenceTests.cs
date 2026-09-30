using AudioTunePro.Core.Models;
using AudioTunePro.Core.Presets;
using AudioTunePro.Core.Services;
using Xunit;

namespace AudioTunePro.Core.Tests;

public class SignalStateCalculatorTests
{
    private const double DefaultCeiling = -0.3;

    [Fact]
    public void Unity_gain_is_safe_with_the_default_ceiling()
    {
        var limit = SignalStateCalculator.BoostLimitDb(true, DefaultCeiling);
        Assert.Equal(SignalState.Safe, SignalStateCalculator.FromBoost(0, limit));
    }

    [Fact]
    public void A_flat_curve_reads_safe_on_the_peak_badge_too()
    {
        var engine = new EqEngine();
        var limit = SignalStateCalculator.BoostLimitDb(engine.Limiter.AutoGainProtection, engine.Limiter.CeilingDb);
        Assert.Equal(SignalState.Safe, SignalStateCalculator.FromBoost(AutoGainLimiter.EstimatePeakBoostDb(engine), limit));
    }

    [Theory]
    [InlineData(0.0, SignalState.Safe)]
    [InlineData(6.0, SignalState.Safe)]
    [InlineData(9.0, SignalState.Warn)]
    [InlineData(11.0, SignalState.Danger)]
    [InlineData(12.0, SignalState.Danger)]
    public void Boost_climbs_safe_warn_danger_against_the_limiter_budget(double boostDb, SignalState expected)
    {
        var limit = SignalStateCalculator.BoostLimitDb(true, DefaultCeiling);
        Assert.Equal(expected, SignalStateCalculator.FromBoost(boostDb, limit));
    }

    [Fact]
    public void Lowering_the_ceiling_shrinks_the_budget()
    {
        Assert.True(SignalStateCalculator.BoostLimitDb(true, -6) < SignalStateCalculator.BoostLimitDb(true, 0));
        Assert.Equal(SignalState.Danger,
            SignalStateCalculator.FromBoost(6, SignalStateCalculator.BoostLimitDb(true, -6)));
    }

    [Fact]
    public void Without_protection_the_fixed_unprotected_limit_applies_whatever_the_ceiling()
    {
        Assert.Equal(SignalStateCalculator.UnprotectedBoostLimitDb, SignalStateCalculator.BoostLimitDb(false, -6));
        Assert.Equal(SignalStateCalculator.UnprotectedBoostLimitDb, SignalStateCalculator.BoostLimitDb(false, 0));
    }
}

public class EqEngineStateTests
{
    [Fact]
    public void Clone_is_equivalent_and_a_changed_band_is_not()
    {
        var engine = PresetLibrary.BuiltIns.First(p => p.Name == "Rock").Engine;
        var clone = engine.Clone();
        Assert.True(engine.IsEquivalentTo(clone));

        clone.Bands[3].GainDb += 0.5;
        Assert.False(engine.IsEquivalentTo(clone));
    }

    [Fact]
    public void Surround_and_limiter_changes_count_as_modifications()
    {
        var a = new EqEngine();
        var b = a.Clone();
        b.Surround.Amount = 0.9;
        Assert.False(a.IsEquivalentTo(b));

        b = a.Clone();
        b.Limiter.AutoGainProtection = false;
        Assert.False(a.IsEquivalentTo(b));
    }

    [Fact]
    public void Sanitized_repairs_wrong_band_count_and_out_of_range_values()
    {
        var broken = new EqEngine
        {
            Bands = new List<EqBand> { new(31, 99), new(62, double.NaN) },
            BassDb = 50,
            TrebleDb = -50,
            PreampDb = -99,
        };
        broken.Limiter.CeilingDb = 5;
        broken.Surround.Amount = 3;
        broken.Surround.Mode = (SurroundMode)42;

        var fixedEngine = broken.Sanitized();

        Assert.Equal(EqEngine.StandardBandFrequencies.Length, fixedEngine.Bands.Count);
        Assert.Equal(12, fixedEngine.Bands[0].GainDb);
        Assert.Equal(0, fixedEngine.Bands[1].GainDb);
        Assert.Equal(0, fixedEngine.Bands[9].GainDb);
        Assert.Equal(12, fixedEngine.BassDb);
        Assert.Equal(-12, fixedEngine.TrebleDb);
        Assert.Equal(-24, fixedEngine.PreampDb);
        Assert.Equal(0, fixedEngine.Limiter.CeilingDb);
        Assert.Equal(1, fixedEngine.Surround.Amount);
        Assert.Equal(SurroundMode.Auto, fixedEngine.Surround.Mode);
    }
}

public class LiveStatePersistenceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "AudioTuneProLive_" + Guid.NewGuid());

    [Fact]
    public void Live_engine_and_tray_hint_round_trip()
    {
        var store = new AppDataStore(_tempDir);
        var live = new EqEngine { BassDb = 4.5 };
        live.Bands[2].GainDb = -3.5;

        Assert.True(store.SaveSettings(new AppSettings { LiveEngine = live, TrayHintShown = true }));
        var loaded = store.LoadSettings();

        Assert.True(loaded.TrayHintShown);
        Assert.NotNull(loaded.LiveEngine);
        Assert.True(live.IsEquivalentTo(loaded.LiveEngine!));
    }

    [Fact]
    public void Saving_reports_failure_instead_of_throwing_when_the_location_is_unusable()
    {
        Directory.CreateDirectory(_tempDir);
        var blocker = Path.Combine(_tempDir, "not-a-folder");
        File.WriteAllText(blocker, "x");

        var store = new AppDataStore(blocker); // a file where the data folder should be

        Assert.False(store.SaveSettings(new AppSettings()));
        Assert.False(store.SaveUserPresets(Array.Empty<Preset>()));
        Assert.NotNull(store.LoadSettings());
    }

    [Fact]
    public void A_corrupt_settings_file_falls_back_to_defaults()
    {
        var store = new AppDataStore(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "settings.json"), "{ not json");

        Assert.Equal(new AppSettings().ActivePresetName, store.LoadSettings().ActivePresetName);
    }

    [Fact]
    public void Writing_leaves_no_temp_file_behind()
    {
        var store = new AppDataStore(_tempDir);
        Assert.True(store.SaveSettings(new AppSettings()));
        Assert.Empty(Directory.GetFiles(_tempDir, "*.tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }
}
