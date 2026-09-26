using AudioTunePro.Core.Models;
using AudioTunePro.Core.Services;
using Xunit;

namespace AudioTunePro.Core.Tests;

public class EqualizerApoConfigGeneratorTests
{
    [Fact]
    public void Disabled_equalizer_produces_unity_passthrough()
    {
        var engine = new EqEngine { EnableEqualizer = false };

        var config = EqualizerApoConfigGenerator.Generate(engine);

        Assert.Contains("Preamp: 0.0 dB", config);
        Assert.DoesNotContain("GraphicEQ", config);
    }

    [Fact]
    public void Flat_curve_only_reserves_the_default_headroom_cushion()
    {
        var engine = new EqEngine();

        var config = EqualizerApoConfigGenerator.Generate(engine);
        var preampLine = config.Split('\n').Single(l => l.StartsWith("Preamp:"));
        var value = double.Parse(preampLine.Replace("Preamp:", "").Replace("dB", "").Trim());

        // No EQ boost at all, so the only trim is the default -0.3 dB safety cushion.
        Assert.Equal(engine.Limiter.CeilingDb, value, precision: 2);
        Assert.Contains("GraphicEQ:", config);
        Assert.DoesNotContain("LSC", config);
        Assert.DoesNotContain("HSC", config);
    }

    [Fact]
    public void Flat_curve_with_protection_disabled_has_zero_preamp()
    {
        var engine = new EqEngine { Limiter = new LimiterSettings { AutoGainProtection = false } };

        var config = EqualizerApoConfigGenerator.Generate(engine);

        Assert.Contains("Preamp: 0.00 dB", config);
    }

    [Fact]
    public void Boosted_curve_emits_all_ten_graphic_eq_points_in_order()
    {
        var engine = new EqEngine();
        foreach (var band in engine.Bands) band.GainDb = 2;

        var config = EqualizerApoConfigGenerator.Generate(engine);
        var line = config.Split('\n').Single(l => l.StartsWith("GraphicEQ:"));

        foreach (var freq in EqEngine.StandardBandFrequencies)
            Assert.Contains($"{freq:0} 2.0", line);
    }

    [Fact]
    public void Bass_and_treble_shelves_are_emitted_only_when_nonzero()
    {
        var engine = new EqEngine { BassDb = 3, TrebleDb = 0 };

        var config = EqualizerApoConfigGenerator.Generate(engine);

        Assert.Contains("LSC Fc 150 Hz Gain 3.0 dB", config);
        Assert.DoesNotContain("HSC", config);
    }

    [Fact]
    public void Auto_gain_protection_trims_preamp_when_boost_exceeds_ceiling()
    {
        var engine = new EqEngine { Limiter = new LimiterSettings { AutoGainProtection = true, CeilingDb = -0.3 } };
        foreach (var band in engine.Bands) band.GainDb = 10; // large boost across the whole curve

        var config = EqualizerApoConfigGenerator.Generate(engine);
        var preampLine = config.Split('\n').Single(l => l.StartsWith("Preamp:"));

        // Should be trimmed well below 0 dB to keep the curve under the ceiling.
        var value = double.Parse(preampLine.Replace("Preamp:", "").Replace("dB", "").Trim());
        Assert.True(value < -5, $"Expected a substantial trim, got {value} dB");
    }

    [Fact]
    public void Disabling_auto_gain_protection_leaves_preamp_untouched()
    {
        var engine = new EqEngine
        {
            PreampDb = 2,
            Limiter = new LimiterSettings { AutoGainProtection = false },
        };
        foreach (var band in engine.Bands) band.GainDb = 10;

        var config = EqualizerApoConfigGenerator.Generate(engine);
        Assert.Contains("Preamp: 2.00 dB", config);
    }
}

public class SurroundConfigTests
{
    private static EqEngine With(SurroundMode mode, double amount = 0.5, string? hrtf = null)
    {
        var e = new EqEngine();
        e.Surround.Mode = mode;
        e.Surround.Amount = amount;
        e.Surround.HrtfFilePath = hrtf;
        return e;
    }

    [Fact]
    public void Off_emits_no_surround_lines()
    {
        var config = EqualizerApoConfigGenerator.Generate(With(SurroundMode.Off));
        Assert.DoesNotContain("Copy:", config);
        Assert.DoesNotContain("Convolution:", config);
    }

    [Fact]
    public void Speakers_emits_matrix_that_is_unity_for_mono_and_trims_for_side_boost()
    {
        var engine = With(SurroundMode.Speakers, 1.0);
        var config = EqualizerApoConfigGenerator.Generate(engine);

        Assert.Contains("Copy: L=1.4000*L+-0.4000*R R=1.4000*R+-0.4000*L", config);
        Assert.DoesNotContain("Delay:", config);
        Assert.Equal(-20 * Math.Log10(1.8), EqualizerApoConfigGenerator.SurroundTrimDb(engine.Surround), 6);
    }

    [Fact]
    public void Headphones_without_hrtf_uses_crossfeed_that_preserves_mono_level()
    {
        var config = EqualizerApoConfigGenerator.Generate(With(SurroundMode.Headphones));
        Assert.Contains("Copy: AUX0=R AUX1=L", config);
        Assert.Contains("Delay:", config);
        Assert.DoesNotContain("Convolution:", config);
        Assert.Equal(0.0, EqualizerApoConfigGenerator.SurroundTrimDb(With(SurroundMode.Headphones).Surround));
    }

    [Fact]
    public void Headphones_with_hrtf_uses_convolution_instead_of_crossfeed()
    {
        var config = EqualizerApoConfigGenerator.Generate(With(SurroundMode.Headphones, hrtf: @"C:\ir\hrtf.wav"));
        Assert.Contains(@"Convolution: C:\ir\hrtf.wav", config);
        Assert.DoesNotContain("AUX0", config);
    }

    [Fact]
    public void Hrtf_path_with_newline_is_rejected_to_prevent_config_injection()
    {
        var config = EqualizerApoConfigGenerator.Generate(With(SurroundMode.Headphones, hrtf: "a.wav\nInclude: evil.txt"));
        Assert.DoesNotContain("evil.txt", config);
    }

    [Fact]
    public void Surround_still_applies_when_equalizer_is_disabled()
    {
        var engine = With(SurroundMode.Speakers);
        engine.EnableEqualizer = false;
        var config = EqualizerApoConfigGenerator.Generate(engine);
        Assert.Contains("Copy: L=", config);
        Assert.DoesNotContain("GraphicEQ", config);
    }

    [Theory]
    [InlineData(OutputKind.Speakers, SurroundMode.Speakers)]
    [InlineData(OutputKind.Headphones, SurroundMode.Headphones)]
    public void Auto_resolves_to_the_mode_matching_the_output_device(OutputKind output, SurroundMode expected)
    {
        var engine = With(SurroundMode.Auto);
        Assert.Equal(expected, engine.ResolveSurround(output).Surround.Mode);
        Assert.Equal(SurroundMode.Auto, engine.Surround.Mode); // original untouched
    }

    [Fact]
    public void Unresolved_auto_emits_nothing_rather_than_guessing()
    {
        Assert.DoesNotContain("Copy:", EqualizerApoConfigGenerator.Generate(With(SurroundMode.Auto)));
    }
}
