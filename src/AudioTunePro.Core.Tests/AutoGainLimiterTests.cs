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
    public void Trim_brings_peak_down_to_ceiling()
    {
        var engine = new EqEngine { Limiter = new LimiterSettings { CeilingDb = -1 } };
        foreach (var band in engine.Bands) band.GainDb = 8;

        double trim = AutoGainLimiter.ComputeTrimDb(engine);
        double resultingPeak = AutoGainLimiter.EstimatePeakBoostDb(engine) + trim;

        Assert.Equal(-1, resultingPeak, precision: 3);
    }
}
