using AudioTunePro.Core.Models;
using AudioTunePro.Core.Services;
using Xunit;

namespace AudioTunePro.Core.Tests;

public class AutoGainLimiterTests
{
    [Fact]
    public void Flat_curve_has_zero_estimated_peak()
    {
        var engine = new EqEngine();
        Assert.Equal(0, AutoGainLimiter.EstimatePeakBoostDb(engine));
    }

    [Fact]
    public void Single_band_boost_is_reinforced_by_its_neighbors()
    {
        var engine = new EqEngine();
        engine.Bands[4].GainDb = 6; // 500 Hz
        engine.Bands[3].GainDb = 3; // 250 Hz neighbor
        engine.Bands[5].GainDb = 3; // 1000 Hz neighbor

        double peak = AutoGainLimiter.EstimatePeakBoostDb(engine);

        // 6 + 0.35*3 + 0.35*3 = 8.1
        Assert.Equal(8.1, peak, precision: 3);
    }

    [Fact]
    public void Trim_is_zero_when_protection_disabled()
    {
        var engine = new EqEngine { Limiter = new LimiterSettings { AutoGainProtection = false } };
        foreach (var band in engine.Bands) band.GainDb = 12;

        Assert.Equal(0, AutoGainLimiter.ComputeTrimDb(engine));
    }

    [Fact]
    public void Trim_never_boosts_only_reduces()
    {
        var engine = new EqEngine { Limiter = new LimiterSettings { CeilingDb = 6 } }; // generous ceiling
        engine.Bands[0].GainDb = 1;

        Assert.Equal(0, AutoGainLimiter.ComputeTrimDb(engine));
    }

    [Fact]
    public void A_ceiling_below_zero_trims_even_a_flat_curve()
    {
        // Documented behavior the UI now surfaces: the ceiling is headroom, not only a boost limiter.
        var engine = new EqEngine { Limiter = new LimiterSettings { CeilingDb = -3 } };

        Assert.Equal(-3, AutoGainLimiter.ComputeTrimDb(engine), precision: 6);
    }

    [Theory]
    [InlineData(true, true, SurroundMode.Off, -3.0)]
    [InlineData(false, true, SurroundMode.Off, 0.0)]            // protection off: nothing applied
    [InlineData(true, false, SurroundMode.Off, 0.0)]            // EQ bypassed, no surround
    public void TotalTrimDb_matches_what_the_generator_writes(bool protection, bool eq, SurroundMode mode, double expected)
    {
        var engine = new EqEngine
        {
            EnableEqualizer = eq,
            Limiter = new LimiterSettings { AutoGainProtection = protection, CeilingDb = -3 },
        };
        engine.Surround.Mode = mode;

        Assert.Equal(expected, EqualizerApoConfigGenerator.TotalTrimDb(engine), precision: 6);
        Assert.Equal(expected, WrittenPreamp(engine) - engine.PreampDb, precision: 2);
    }

    [Fact]
    public void TotalTrimDb_includes_the_speaker_widening_trim_even_with_the_equalizer_off()
    {
        var engine = new EqEngine { EnableEqualizer = false, Limiter = new LimiterSettings { CeilingDb = 0 } };
        engine.Surround.Mode = SurroundMode.Speakers;
        engine.Surround.Amount = 1.0;

        double trim = EqualizerApoConfigGenerator.TotalTrimDb(engine);

        Assert.Equal(-20 * Math.Log10(1.8), trim, precision: 6);
        Assert.Equal(trim, WrittenPreamp(engine), precision: 2);
    }

    [Fact]
    public void TotalTrimDb_for_a_boosted_curve_matches_the_written_preamp()
    {
        var engine = new EqEngine { PreampDb = 2 };
        foreach (var band in engine.Bands) band.GainDb = 6;
        engine.Surround.Mode = SurroundMode.Speakers;

        Assert.Equal(EqualizerApoConfigGenerator.TotalTrimDb(engine),
            WrittenPreamp(engine) - engine.PreampDb, precision: 2);
    }

    private static double WrittenPreamp(EqEngine engine)
    {
        var line = EqualizerApoConfigGenerator.Generate(engine).Split('\n').First(l => l.StartsWith("Preamp:"));
        return double.Parse(line.Replace("Preamp:", "").Replace("dB", "").Trim(), System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Trim_brings_peak_down_to_ceiling()
    {
        var engine = new EqEngine { Limiter = new LimiterSettings { CeilingDb = -1 } };
        foreach (var band in engine.Bands) band.GainDb = 8;

        double trim = AutoGainLimiter.ComputeTrimDb(engine);
        double resultingPeak = AutoGainLimiter.EstimatePeakBoostDb(engine) + trim;

        Assert.Equal(-1, resultingPeak, precision: 3);
    }
}
