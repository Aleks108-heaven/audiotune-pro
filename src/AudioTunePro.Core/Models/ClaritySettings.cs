namespace AudioTunePro.Core.Models;

/// <summary>
/// "Clear sound" controls that sit on top of the graphic EQ, so the band faders stay the user's own:
/// <list type="bullet">
/// <item><b>Clarity</b> (0..1) blends in three gentle filters: a cut in the low-mid mud around 300 Hz, a lift in
/// the speech-presence region around 3 kHz, and a little air above 10 kHz.</item>
/// <item><b>Low-cut</b> is a high-pass filter. Small laptop speakers cannot reproduce deep bass; feeding it to them
/// only adds distortion that smears the mids, so cutting it below the speaker's range sounds cleaner.</item>
/// </list>
/// Both are fixed, documented curves rather than guesses at the user's hardware.
/// </summary>
public sealed class ClaritySettings
{
    // Filter shapes at Amount = 1. Gains scale linearly with Amount.
    public const double MudFcHz = 300;
    public const double MudCutDb = -2.5;
    public const double MudQ = 0.9;

    public const double PresenceFcHz = 3200;
    public const double PresenceBoostDb = 2.0;
    public const double PresenceQ = 0.8;

    public const double AirFcHz = 10000;
    public const double AirBoostDb = 1.5;
    public const double AirQ = 0.7;

    /// <summary>Lowest corner the Low-cut slider can pick; below it the filter is off.</summary>
    public const double MinLowCutHz = 30;
    public const double MaxLowCutHz = 150;

    /// <summary>Strength of the clarity curve, 0 (off) to 1.</summary>
    public double Amount { get; set; }

    /// <summary>High-pass corner in Hz; 0 means off. Values between 0 and <see cref="MinLowCutHz"/> are treated as off.</summary>
    public double LowCutHz { get; set; }

    /// <summary>
    /// Worst-case boost (dB) the clarity filters can add at any one frequency, for the limiter. The presence peak and
    /// the air shelf sit far enough apart that their sum never exceeds the larger of the two by more than a fraction,
    /// so the larger one is used (the cut and the high-pass never add gain).
    /// </summary>
    public static double WorstCaseBoostDb(double amount) =>
        Math.Clamp(double.IsFinite(amount) ? amount : 0, 0, 1) * Math.Max(PresenceBoostDb, AirBoostDb);

    public static bool IsLowCutOn(double lowCutHz) => double.IsFinite(lowCutHz) && lowCutHz >= MinLowCutHz;

    public ClaritySettings Clone() => new()
    {
        Amount = Amount,
        LowCutHz = LowCutHz,
    };
}
