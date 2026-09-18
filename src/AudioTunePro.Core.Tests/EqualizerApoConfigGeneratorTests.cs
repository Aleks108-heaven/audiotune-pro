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
