using AudioTunePro.Core.Models;

namespace AudioTunePro.Core.Services;

/// <summary>
/// Computes a safe preamp trim so that EQ boosts can't push the signal into
/// digital clipping (0 dBFS). Equalizer APO has no built-in lookahead/brickwall
/// limiter, so AudioTune Pro protects against clipping the reliable way real
/// graphic EQs use: measure the worst-case constructive boost across the curve
/// and pull the master gain down to compensate, ahead of time.
/// </summary>
public static class AutoGainLimiter
{
    /// <summary>
    /// Estimates the worst-case peak boost (in dB) the current curve could produce,
    /// accounting for adjacent graphic-EQ bands reinforcing each other at their
    /// shared crossover point (a fixed overlap factor for the standard Q used by
    /// <see cref="EqualizerApoConfigGenerator"/>'s GraphicEQ spline).
    /// </summary>
    public const double AdjacentBandOverlap = 0.35;
    public const double ShelfOverlapWithBands = 0.5;

    public static double EstimatePeakBoostDb(EqEngine engine)
    {
        var gains = engine.Bands.Select(b => b.GainDb).ToArray();
        double worst = 0;

        for (int i = 0; i < gains.Length; i++)
        {
            double local = gains[i];
            if (i > 0) local += Math.Max(0, gains[i - 1]) * AdjacentBandOverlap;
            if (i < gains.Length - 1) local += Math.Max(0, gains[i + 1]) * AdjacentBandOverlap;
            worst = Math.Max(worst, local);
        }

        worst += Math.Max(0, engine.BassDb) * ShelfOverlapWithBands;
        worst += Math.Max(0, engine.TrebleDb) * ShelfOverlapWithBands;
        worst += engine.PreampDb;

        return Math.Max(0, worst);
    }

    /// <summary>
    /// Returns the preamp trim (a value &lt;= 0 dB) to apply on top of the user's
    /// preamp so the estimated peak never exceeds <see cref="LimiterSettings.CeilingDb"/>.
    /// Returns 0 when protection is disabled or no trim is needed.
    /// </summary>
    public static double ComputeTrimDb(EqEngine engine)
    {
        if (!engine.Limiter.AutoGainProtection) return 0;

        double peak = EstimatePeakBoostDb(engine);
        double overshoot = peak - engine.Limiter.CeilingDb;
        return overshoot > 0 ? -overshoot : 0;
    }
}
