using System.Globalization;

namespace AudioTunePro.Core.Services;

/// <summary>
/// Level-meter mapping and ballistics: peak dBFS is mapped onto a 0..1 fill over
/// <see cref="FloorDb"/>..<see cref="CeilingDb"/> and falls at a fixed rate (instant attack,
/// ~1.1 s to fall across the full range at 30 fps).
/// </summary>
public static class LevelMeterMath
{
    public const double FloorDb = -18.0;
    public const double CeilingDb = 6.0; // over-range headroom above full scale
    public const float FallPerTick = 0.03f;

    /// <summary>Linear peak (1.0 = 0 dBFS) to a 0..1 fill; silence or invalid input gives 0.</summary>
    public static float ToFill(float linearPeak)
    {
        if (!(linearPeak > 0f) || float.IsInfinity(linearPeak)) return 0f;
        double db = 20.0 * Math.Log10(linearPeak);
        return (float)Math.Clamp((db - FloorDb) / (CeilingDb - FloorDb), 0.0, 1.0);
    }

    /// <summary>Instant attack, fixed-rate release.</summary>
    public static float Step(float current, float target) =>
        target >= current ? target : Math.Max(target, current - FallPerTick);

    /// <summary>Numeric readout for a fill value, e.g. "-12.4 dB"; "— dB" when empty.</summary>
    public static string FormatText(float fill) => fill <= 0f
        ? "— dB"
        : string.Create(CultureInfo.InvariantCulture,
            $"{fill * (CeilingDb - FloorDb) + FloorDb:+0.0;-0.0;0.0} dB");
}
