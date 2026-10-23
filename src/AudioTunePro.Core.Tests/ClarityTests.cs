using System.Globalization;
using AudioTunePro.Core.Models;
using AudioTunePro.Core.Presets;
using AudioTunePro.Core.Services;
using Xunit;

namespace AudioTunePro.Core.Tests;

public class ClarityConfigTests
{
    private static string[] Lines(EqEngine engine) =>
        EqualizerApoConfigGenerator.Generate(engine).Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

    private static double WrittenPreamp(EqEngine engine) =>
        double.Parse(Lines(engine).First(l => l.StartsWith("Preamp:")).Replace("Preamp:", "").Replace("dB", "").Trim(),
            CultureInfo.InvariantCulture);

    [Fact]
    public void Clarity_and_low_cut_are_off_by_default_and_write_nothing()
    {
        var lines = Lines(new EqEngine());

        Assert.DoesNotContain(lines, l => l.Contains(" PK ") || l.Contains(" HP "));
        Assert.DoesNotContain(lines, l => l.Contains("Clarity") || l.Contains("Low-cut"));
    }

    [Fact]
    public void Full_clarity_writes_the_mud_cut_presence_peak_and_air_shelf()
    {
        var engine = new EqEngine();
        engine.Clarity.Amount = 1.0;

        var lines = Lines(engine);

        Assert.Contains("Filter 1: ON PK Fc 300 Hz Gain -2.5 dB Q 0.9", lines);
        Assert.Contains("Filter 2: ON PK Fc 3200 Hz Gain 2.0 dB Q 0.8", lines);
        Assert.Contains("Filter 3: ON HSC Fc 10000 Hz Gain 1.5 dB Q 0.7", lines);
    }

    [Fact]
    public void Clarity_scales_its_gains_with_the_amount()
    {
        var engine = new EqEngine();
        engine.Clarity.Amount = 0.4;

        var lines = Lines(engine);

        Assert.Contains("Filter 1: ON PK Fc 300 Hz Gain -1.0 dB Q 0.9", lines);
        Assert.Contains("Filter 2: ON PK Fc 3200 Hz Gain 0.8 dB Q 0.8", lines);
        Assert.Contains("Filter 3: ON HSC Fc 10000 Hz Gain 0.6 dB Q 0.7", lines);
    }

    [Fact]
    public void Filters_are_numbered_after_the_bass_and_treble_shelves_and_the_low_cut_comes_last()
    {
        var engine = new EqEngine { BassDb = 2, TrebleDb = 2 };
        engine.Clarity.Amount = 1.0;
        engine.Clarity.LowCutHz = 80;

        var lines = Lines(engine);

        Assert.Contains("Filter 1: ON LSC Fc 150 Hz Gain 2.0 dB Q 0.9", lines);
        Assert.Contains("Filter 2: ON HSC Fc 6500 Hz Gain 2.0 dB Q 0.9", lines);
        Assert.Contains("Filter 3: ON PK Fc 300 Hz Gain -2.5 dB Q 0.9", lines);
        Assert.Contains("Filter 6: ON HP Fc 80 Hz", lines);
    }

    [Theory]
    [InlineData(0.0, false, 0)]
    [InlineData(10.0, false, 0)]   // below the slider's range: off, not a 10 Hz filter
    [InlineData(29.9, false, 0)]
    [InlineData(30.0, true, 30)]
    [InlineData(80.0, true, 80)]
    [InlineData(150.0, true, 150)]
    [InlineData(500.0, true, 150)] // clamped to the top of the range
    public void The_low_cut_is_a_high_pass_only_inside_its_range(double hz, bool expected, double written)
    {
        var engine = new EqEngine();
        engine.Clarity.LowCutHz = hz;

        var lines = Lines(engine);

        if (expected) Assert.Contains($"Filter 1: ON HP Fc {written:0} Hz", lines);
        else Assert.DoesNotContain(lines, l => l.Contains(" HP "));
    }

    [Fact]
    public void Bypassing_the_equalizer_bypasses_clarity_and_the_low_cut_too()
    {
        var engine = new EqEngine { EnableEqualizer = false };
        engine.Clarity.Amount = 1.0;
        engine.Clarity.LowCutHz = 80;

        var lines = Lines(engine);

        Assert.DoesNotContain(lines, l => l.StartsWith("Filter"));
    }

    [Fact]
    public void Non_finite_clarity_values_never_reach_the_config()
    {
        var engine = new EqEngine();
        engine.Clarity.Amount = double.NaN;
        engine.Clarity.LowCutHz = double.PositiveInfinity;

        var config = EqualizerApoConfigGenerator.Generate(engine);

        Assert.DoesNotContain("NaN", config);
        Assert.DoesNotContain("Infinity", config);
        Assert.DoesNotContain("Filter", config);
    }

    [Fact]
    public void The_limiter_reserves_headroom_for_the_clarity_boost()
    {
        var engine = new EqEngine();
        engine.Clarity.Amount = 1.0; // up to +2 dB at the presence peak

        Assert.Equal(2.0, AutoGainLimiter.EstimatePeakBoostDb(engine), 6);
        // 2.0 dB of boost against the -0.3 dB ceiling: trim 2.3 dB, and the Preamp line says so.
        Assert.Equal(-2.3, AutoGainLimiter.ComputeTrimDb(engine), 6);
        Assert.Equal(-2.3, WrittenPreamp(engine), 2);
        Assert.Equal(-2.3, EqualizerApoConfigGenerator.TotalTrimDb(engine), 6);
    }

    [Fact]
    public void The_low_cut_alone_needs_no_extra_headroom()
    {
        var engine = new EqEngine();
        engine.Clarity.LowCutHz = 100;

        Assert.Equal(0, AutoGainLimiter.EstimatePeakBoostDb(engine));
    }

    [Fact]
    public void A_boosted_band_at_the_presence_peak_still_stays_under_the_ceiling()
    {
        var engine = EqEngine.Create(31);
        engine.Bands.Single(b => b.FrequencyHz == 3150).GainDb = 6;
        engine.Clarity.Amount = 1.0;

        double peak = AutoGainLimiter.EstimatePeakBoostDb(engine);

        Assert.True(peak >= 6 + 2.0, $"6 dB band + 2 dB presence peak = 8 dB at 3.15 kHz; estimated {peak}");
        Assert.Equal(engine.Limiter.CeilingDb, peak + AutoGainLimiter.ComputeTrimDb(engine), 6);
    }
}

public class ClarityStateTests
{
    [Fact]
    public void Sanitizing_pulls_clarity_values_into_range()
    {
        var engine = new EqEngine();
        engine.Clarity.Amount = 7;
        engine.Clarity.LowCutHz = 9999;
        var clean = engine.Sanitized();
        Assert.Equal(1, clean.Clarity.Amount);
        Assert.Equal(ClaritySettings.MaxLowCutHz, clean.Clarity.LowCutHz);

        engine.Clarity.Amount = -1;
        engine.Clarity.LowCutHz = 5;
        clean = engine.Sanitized();
        Assert.Equal(0, clean.Clarity.Amount);
        Assert.Equal(0, clean.Clarity.LowCutHz);

        engine.Clarity.Amount = double.NaN;
        engine.Clarity.LowCutHz = double.NaN;
        clean = engine.Sanitized();
        Assert.Equal(0, clean.Clarity.Amount);
        Assert.Equal(0, clean.Clarity.LowCutHz);
    }

    [Fact]
    public void Clone_and_equivalence_include_clarity()
    {
        var a = new EqEngine();
        a.Clarity.Amount = 0.5;
        a.Clarity.LowCutHz = 70;

        var clone = a.Clone();
        Assert.True(a.IsEquivalentTo(clone));
        Assert.NotSame(a.Clarity, clone.Clarity);

        clone.Clarity.Amount = 0.7;
        Assert.False(a.IsEquivalentTo(clone));

        clone = a.Clone();
        clone.Clarity.LowCutHz = 90;
        Assert.False(a.IsEquivalentTo(clone));
    }

    [Fact]
    public void A_low_cut_below_its_range_counts_as_off_when_comparing()
    {
        var a = new EqEngine();
        var b = new EqEngine();
        b.Clarity.LowCutHz = 12; // meaningless value, same sound as off

        Assert.True(a.IsEquivalentTo(b));
    }

    [Fact]
    public void Reset_turns_clarity_and_the_low_cut_off()
    {
        var engine = new EqEngine { BassDb = 3 };
        engine.Bands[2].GainDb = 4;
        engine.Clarity.Amount = 0.8;
        engine.Clarity.LowCutHz = 90;

        engine.Reset();

        Assert.True(engine.IsEquivalentTo(new EqEngine { Surround = engine.Surround.Clone() }));
        Assert.Equal(0, engine.Clarity.Amount);
        Assert.Equal(0, engine.Clarity.LowCutHz);
    }
}

public class ClearSoundPresetTests
{
    [Fact]
    public void The_clear_sound_presets_are_built_in_and_use_the_clarity_controls()
    {
        var clear = PresetLibrary.BuiltIns.Single(p => p.Name == "Clear Sound");
        var laptop = PresetLibrary.BuiltIns.Single(p => p.Name == "Clear Sound (Laptop Speakers)");

        Assert.True(clear.IsBuiltIn && laptop.IsBuiltIn);
        Assert.True(clear.Engine.Clarity.Amount > 0);
        Assert.Equal(0, clear.Engine.Clarity.LowCutHz);            // headphones keep their real bass
        Assert.True(ClaritySettings.IsLowCutOn(laptop.Engine.Clarity.LowCutHz));
    }

    [Fact]
    public void Existing_presets_sound_exactly_as_before_clarity_is_off_in_all_of_them()
    {
        foreach (var p in PresetLibrary.BuiltIns.Where(p => !p.Name.StartsWith("Clear Sound")))
        {
            Assert.Equal(0, p.Engine.Clarity.Amount);
            Assert.Equal(0, p.Engine.Clarity.LowCutHz);
        }
    }

    [Fact]
    public void Every_built_in_preset_is_already_inside_every_range()
    {
        Assert.Equal(PresetLibrary.BuiltIns.Count, PresetLibrary.BuiltIns.Select(p => p.Name).Distinct().Count());
        foreach (var p in PresetLibrary.BuiltIns)
        {
            Assert.True(p.Engine.IsEquivalentTo(p.Engine.Sanitized()), $"{p.Name} has an out-of-range value");
            Assert.True(p.Name.Length <= 60, $"{p.Name} is longer than a preset name may be");
        }
    }
}
