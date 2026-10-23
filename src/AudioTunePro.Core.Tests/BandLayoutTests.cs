using System.Globalization;
using System.Text.Json;
using AudioTunePro.Core.Models;
using AudioTunePro.Core.Presets;
using AudioTunePro.Core.Services;
using Xunit;

namespace AudioTunePro.Core.Tests;

public class BandLayoutTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(31)]
    public void A_layout_is_strictly_ascending_and_inside_the_audible_range(int count)
    {
        var f = BandLayouts.Frequencies(count);

        Assert.Equal(count, f.Length);
        Assert.True(f.Zip(f.Skip(1), (a, b) => b > a).All(x => x), "no two bands may share a frequency");
        Assert.InRange(f[0], 20, 31);
        Assert.InRange(f[^1], 16000, 20000);
    }

    [Fact]
    public void The_ten_band_layout_is_the_original_one()
    {
        Assert.Equal(EqEngine.StandardBandFrequencies, BandLayouts.Frequencies(10));
        Assert.Equal(new[] { 31.0, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 }, EqEngine.StandardBandFrequencies);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(32)]
    public void An_unsupported_count_is_refused(int count)
    {
        Assert.False(BandLayouts.IsSupported(count));
        Assert.Throws<ArgumentOutOfRangeException>(() => BandLayouts.Frequencies(count));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EqEngine().WithBandCount(count));
    }

    [Fact]
    public void Frequencies_returns_a_copy_that_cannot_corrupt_the_layout()
    {
        var f = BandLayouts.Frequencies(15);
        f[0] = 9999;

        Assert.Equal(25, BandLayouts.Frequencies(15)[0]);
    }

    [Theory]
    [InlineData(10, 15)]
    [InlineData(10, 31)]
    [InlineData(15, 31)]
    [InlineData(31, 15)]
    [InlineData(31, 10)]
    [InlineData(15, 10)]
    public void A_flat_curve_stays_flat_on_every_layout(int from, int to)
    {
        var converted = EqEngine.Create(from).WithBandCount(to);

        Assert.Equal(to, converted.Bands.Count);
        Assert.All(converted.Bands, b => Assert.Equal(0, b.GainDb));
        Assert.Equal(BandLayouts.Frequencies(to), converted.Bands.Select(b => b.FrequencyHz).ToArray());
    }

    [Fact]
    public void Bands_that_exist_in_both_layouts_keep_their_gain()
    {
        var ten = new EqEngine();
        for (int i = 0; i < ten.Bands.Count; i++) ten.Bands[i].GainDb = i - 4; // -4 .. +5

        var thirtyOne = ten.WithBandCount(31);

        foreach (var shared in new double[] { 125, 250, 500, 1000, 2000, 4000, 8000, 16000 })
        {
            double expected = ten.Bands.Single(b => b.FrequencyHz == shared).GainDb;
            Assert.Equal(expected, thirtyOne.Bands.Single(b => b.FrequencyHz == shared).GainDb);
        }
    }

    [Fact]
    public void Gains_between_bands_follow_a_straight_line_over_log_frequency()
    {
        var ten = new EqEngine();
        ten.Bands.Single(b => b.FrequencyHz == 1000).GainDb = 4;
        ten.Bands.Single(b => b.FrequencyHz == 2000).GainDb = 8;

        var thirtyOne = ten.WithBandCount(31);

        // 1250 Hz is log2(1.25) = 0.3219 of the way from 1 kHz to 2 kHz; 1600 Hz is 0.6781 of the way.
        Assert.Equal(5.3, thirtyOne.Bands.Single(b => b.FrequencyHz == 1250).GainDb);
        Assert.Equal(6.7, thirtyOne.Bands.Single(b => b.FrequencyHz == 1600).GainDb);
    }

    [Fact]
    public void Beyond_the_first_and_last_band_the_end_values_are_held()
    {
        var ten = new EqEngine();
        ten.Bands[0].GainDb = 6;    // 31 Hz
        ten.Bands[^1].GainDb = -3;  // 16 kHz

        var thirtyOne = ten.WithBandCount(31);

        Assert.Equal(6, thirtyOne.Bands.Single(b => b.FrequencyHz == 20).GainDb);
        Assert.Equal(6, thirtyOne.Bands.Single(b => b.FrequencyHz == 25).GainDb);
        Assert.Equal(-3, thirtyOne.Bands.Single(b => b.FrequencyHz == 20000).GainDb);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Resampling_never_creates_gain_outside_the_range_of_the_source_curve(int seed)
    {
        var rng = new Random(seed);
        foreach (var from in BandLayouts.SupportedCounts)
        {
            var source = EqEngine.Create(from);
            foreach (var b in source.Bands) b.GainDb = Math.Round(rng.NextDouble() * 24 - 12, 1);
            double min = source.Bands.Min(b => b.GainDb), max = source.Bands.Max(b => b.GainDb);

            foreach (var to in BandLayouts.SupportedCounts)
            {
                var converted = source.WithBandCount(to);
                Assert.All(converted.Bands, b => Assert.InRange(b.GainDb, min - 0.05, max + 0.05));
            }
        }
    }

    [Fact]
    public void Converting_keeps_everything_that_is_not_a_band_and_returns_a_new_object()
    {
        var engine = PresetLibrary.BuiltIns.First(p => p.Name == "Clear Sound (Laptop Speakers)").Engine.Clone();
        engine.Limiter.CeilingDb = -2;

        var converted = engine.WithBandCount(15);
        var same = engine.WithBandCount(10);

        Assert.NotSame(engine, converted);
        Assert.NotSame(engine, same);
        Assert.True(engine.IsEquivalentTo(same));
        Assert.Equal(engine.BassDb, converted.BassDb);
        Assert.Equal(engine.TrebleDb, converted.TrebleDb);
        Assert.Equal(engine.PreampDb, converted.PreampDb);
        Assert.Equal(-2, converted.Limiter.CeilingDb);
        Assert.Equal(engine.Clarity.Amount, converted.Clarity.Amount);
        Assert.Equal(engine.Clarity.LowCutHz, converted.Clarity.LowCutHz);
        Assert.Equal(engine.Surround.Mode, converted.Surround.Mode);
    }

    [Fact]
    public void A_non_finite_gain_is_read_as_flat_when_converting()
    {
        var ten = new EqEngine();
        ten.Bands[4].GainDb = double.NaN;
        ten.Bands[5].GainDb = double.PositiveInfinity;

        var converted = ten.WithBandCount(31);

        Assert.All(converted.Bands, b => Assert.True(double.IsFinite(b.GainDb)));
    }

    [Theory]
    [InlineData(15)]
    [InlineData(31)]
    public void Sanitizing_keeps_a_supported_resolution_and_ignores_stored_frequencies(int count)
    {
        var tampered = EqEngine.Create(count);
        tampered.Bands[2].GainDb = 3.5;
        tampered.Bands[0] = new EqBand(0.001, 99); // out-of-range gain and a frequency nobody may set

        var clean = tampered.Sanitized();

        Assert.Equal(count, clean.Bands.Count);
        Assert.Equal(BandLayouts.Frequencies(count), clean.Bands.Select(b => b.FrequencyHz).ToArray());
        Assert.Equal(12, clean.Bands[0].GainDb);
        Assert.Equal(3.5, clean.Bands[2].GainDb);
    }

    [Fact]
    public void Switching_back_and_forth_on_an_untouched_preset_never_drifts()
    {
        var rock = PresetLibrary.BuiltIns.First(p => p.Name == "Rock").Engine;
        var live = rock.Clone();

        // 10 -> 31 -> 15 -> 10 -> 31 -> 10, as a user flicking the BANDS choice would.
        foreach (var count in new[] { 31, 15, 10, 31, 10 })
            live = BandLayouts.Switch(live, rock, count);

        Assert.True(rock.IsEquivalentTo(live), "an untouched preset must come back exactly");
    }

    [Fact]
    public void Switching_keeps_what_the_user_edited_by_interpolating_it()
    {
        var rock = PresetLibrary.BuiltIns.First(p => p.Name == "Rock").Engine;
        var live = rock.Clone();
        live.Bands.Single(b => b.FrequencyHz == 1000).GainDb = 6;   // edited: no longer the preset

        var on31 = BandLayouts.Switch(live, rock, 31);

        Assert.Equal(31, on31.Bands.Count);
        Assert.Equal(6, on31.Bands.Single(b => b.FrequencyHz == 1000).GainDb);
        Assert.False(on31.IsEquivalentTo(rock.WithBandCount(31)));
    }

    [Fact]
    public void Switching_without_a_selected_preset_interpolates_the_live_curve()
    {
        var live = new EqEngine();
        live.Bands[5].GainDb = 3;

        var on15 = BandLayouts.Switch(live, null, 15);

        Assert.Equal(15, on15.Bands.Count);
        Assert.Equal(3, on15.Bands.Single(b => b.FrequencyHz == 1000).GainDb);
    }

    [Fact]
    public void Switching_returns_a_new_engine_and_leaves_both_inputs_alone()
    {
        var rock = PresetLibrary.BuiltIns.First(p => p.Name == "Rock").Engine;
        var live = rock.Clone();

        var result = BandLayouts.Switch(live, rock, 10);

        Assert.NotSame(live, result);
        Assert.NotSame(rock, result);
        Assert.Equal(10, rock.Bands.Count);
        Assert.Equal(10, live.Bands.Count);
    }

    [Fact]
    public void Sanitizing_an_unsupported_band_count_falls_back_to_the_ten_standard_bands()
    {
        var odd = new EqEngine { Bands = Enumerable.Range(0, 12).Select(i => new EqBand(100 + i * 100, 1)).ToList() };

        var clean = odd.Sanitized();

        Assert.Equal(10, clean.Bands.Count);
        Assert.Equal(EqEngine.StandardBandFrequencies, clean.Bands.Select(b => b.FrequencyHz).ToArray());
        Assert.Equal(1, clean.Bands[0].GainDb); // by position, as before
    }
}

public class BandLayoutConfigTests
{
    private static string[] GraphicEqPoints(string config)
    {
        var line = config.Split('\n').Single(l => l.StartsWith("GraphicEQ:"));
        return line["GraphicEQ:".Length..].Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(31)]
    public void Every_band_becomes_one_strictly_ascending_graphic_eq_point(int count)
    {
        var engine = EqEngine.Create(count);
        for (int i = 0; i < count; i++) engine.Bands[i].GainDb = (i % 5) - 2;

        var points = GraphicEqPoints(EqualizerApoConfigGenerator.Generate(engine));

        Assert.Equal(count, points.Length);
        var freqs = points.Select(p => double.Parse(p.Split(' ')[0], CultureInfo.InvariantCulture)).ToArray();
        Assert.True(freqs.Zip(freqs.Skip(1), (a, b) => b > a).All(x => x), "frequencies must strictly ascend");
        for (int i = 0; i < count; i++)
            Assert.Equal(engine.Bands[i].GainDb, double.Parse(points[i].Split(' ')[1], CultureInfo.InvariantCulture));
    }

    [Fact]
    public void The_thirty_one_band_line_starts_and_ends_where_the_layout_does()
    {
        var points = GraphicEqPoints(EqualizerApoConfigGenerator.Generate(EqEngine.Create(31)));

        Assert.Equal("20 0.0", points[0]);
        Assert.Equal("25 0.0", points[1]);
        Assert.Equal("31.5 0.0", points[2]); // Equalizer APO reads decimal frequencies, so 31.5 Hz is written as is
        Assert.Equal("20000 0.0", points[^1]);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(31)]
    public void Auto_gain_protection_is_never_less_cautious_than_the_loudest_band(int count)
    {
        var engine = EqEngine.Create(count);
        engine.Bands[count / 2].GainDb = 9;

        double peak = AutoGainLimiter.EstimatePeakBoostDb(engine);
        double trim = AutoGainLimiter.ComputeTrimDb(engine);

        Assert.True(peak >= 9);
        Assert.True(peak + trim <= engine.Limiter.CeilingDb + 1e-9);
    }

    [Fact]
    public void Every_built_in_preset_renders_cleanly_on_every_layout()
    {
        foreach (var preset in PresetLibrary.BuiltIns)
        foreach (var count in BandLayouts.SupportedCounts)
        {
            var config = EqualizerApoConfigGenerator.Generate(preset.Engine.WithBandCount(count).ResolveSurround(OutputKind.Speakers));

            Assert.DoesNotContain("NaN", config);
            Assert.DoesNotContain("Infinity", config);
            Assert.Equal(count, GraphicEqPoints(config).Length);
        }
    }

    [Fact]
    public void The_original_ten_band_output_is_unchanged_by_the_new_features()
    {
        var engine = PresetLibrary.BuiltIns.First(p => p.Name == "Rock").Engine;

        var lines = EqualizerApoConfigGenerator.Generate(engine)
            .Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0 && !l.StartsWith('#')).ToArray();

        Assert.Equal(new[]
        {
            // Worked by hand from AutoGainLimiter: worst band 5.05 + bass 2.0 + treble 1.5 + preamp -2 = 6.55 dB peak,
            // trimmed to the -0.3 dB ceiling (-6.85), plus the preamp of -2.
            "Preamp: -8.85 dB",
            "GraphicEQ: 31 4.0; 62 3.0; 125 1.0; 250 -0.5; 500 -1.0; 1000 0.0; 2000 1.0; 4000 2.0; 8000 3.0; 16000 3.5",
            "Filter 1: ON LSC Fc 150 Hz Gain 4.0 dB Q 0.9",
            "Filter 2: ON HSC Fc 6500 Hz Gain 3.0 dB Q 0.9",
        }, lines);
    }
}

public class StoredDataCompatibilityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "AudioTuneProBands_" + Guid.NewGuid());

    [Fact]
    public void Settings_saved_by_the_ten_band_build_still_load_and_start_on_the_default_resolution()
    {
        const string old = """
            {
              "ActivePresetName": "Rock",
              "StartWithWindows": false,
              "LiveEngine": {
                "Bands": [
                  { "FrequencyHz": 31, "GainDb": 4 }, { "FrequencyHz": 62, "GainDb": 3 },
                  { "FrequencyHz": 125, "GainDb": 1 }, { "FrequencyHz": 250, "GainDb": -0.5 },
                  { "FrequencyHz": 500, "GainDb": -1 }, { "FrequencyHz": 1000, "GainDb": 0 },
                  { "FrequencyHz": 2000, "GainDb": 1 }, { "FrequencyHz": 4000, "GainDb": 2 },
                  { "FrequencyHz": 8000, "GainDb": 3 }, { "FrequencyHz": 16000, "GainDb": 3.5 }
                ],
                "BassDb": 4, "TrebleDb": 3, "PreampDb": -2, "EnableEqualizer": true,
                "Limiter": { "AutoGainProtection": true, "CeilingDb": -0.3 },
                "Surround": { "Mode": 0, "Amount": 0.5, "HrtfFilePath": null }
              }
            }
            """;
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), old);

        var loaded = new AppDataStore(_dir).LoadSettings();

        Assert.Equal(0, loaded.BandCount); // "not chosen yet": the app moves it to BandLayouts.DefaultCount
        var live = loaded.LiveEngine!.Sanitized();
        Assert.Equal(10, live.Bands.Count);
        Assert.Equal(0, live.Clarity.Amount);
        Assert.Equal(0, live.Clarity.LowCutHz);

        var upgraded = live.WithBandCount(BandLayouts.DefaultCount);
        Assert.Equal(15, upgraded.Bands.Count);
        Assert.Equal(-0.5, upgraded.Bands.Single(b => b.FrequencyHz == 250).GainDb); // 250 Hz is in both layouts
        Assert.Equal(4.0, upgraded.Bands[0].GainDb); // 25 Hz is below the lowest old band, so its value (31 Hz) is held
    }

    [Fact]
    public void A_thirty_one_band_engine_with_clarity_survives_a_save_and_load()
    {
        var store = new AppDataStore(_dir);
        var live = EqEngine.Create(31);
        live.Bands[10].GainDb = 3.5;
        live.Clarity.Amount = 0.6;
        live.Clarity.LowCutHz = 80;

        Assert.True(store.SaveSettings(new AppSettings { LiveEngine = live, BandCount = 31 }));
        var loaded = store.LoadSettings();

        Assert.Equal(31, loaded.BandCount);
        Assert.True(live.IsEquivalentTo(loaded.LiveEngine!.Sanitized()));
    }

    [Fact]
    public void A_null_clarity_in_a_hand_edited_file_is_repaired_instead_of_crashing()
    {
        var json = JsonSerializer.Serialize(new EqEngine());
        json = json.Replace("\"Clarity\":{\"Amount\":0,\"LowCutHz\":0}", "\"Clarity\":null");
        Assert.Contains("\"Clarity\":null", json);
        var engine = JsonSerializer.Deserialize<EqEngine>(json)!;

        var clean = engine.Sanitized();

        Assert.NotNull(clean.Clarity);
        Assert.Equal(0, clean.Clarity.Amount);
        Assert.NotNull(engine.Clone().Clarity);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
