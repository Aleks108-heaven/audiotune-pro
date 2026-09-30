namespace AudioTunePro.Core.Models;

/// <summary>
/// The three gain-risk states shared by EQ faders, the Limiter's PeakIndicator and the
/// level meter, per design-system/components.md: signal-safe / signal-warn / signal-danger.
/// </summary>
public enum SignalState
{
    Safe,
    Warn,
    Danger,
}

/// <summary>
/// Turns a boost (dB above unity) into one of the three <see cref="SignalState"/> values by
/// comparing it with the "boost budget" the limiter allows, so every control that reads gain
/// risk agrees. Unity gain is always Safe.
/// The design system specifies the three states but not their exact margins; 3 dB / 1 dB
/// were chosen as a caution / final-margin split for this app's +/-12 dB range.
/// </summary>
public static class SignalStateCalculator
{
    public const double FaderRangeDb = 12.0;
    public const double WarnMarginDb = 3.0;
    public const double DangerMarginDb = 1.0;

    /// <summary>
    /// Boost budget when Auto-gain protection is off. Nothing trims the signal then, so a boost
    /// of more than a few dB can clip normal loud masters; 6 dB is a conservative heuristic.
    /// </summary>
    public const double UnprotectedBoostLimitDb = 6.0;

    /// <summary>
    /// The boost at which the limiter is fully used. With protection on it shrinks as the
    /// ceiling is lowered (a -6 dB ceiling leaves half the fader range as budget); with it off
    /// it is the fixed unprotected limit.
    /// </summary>
    public static double BoostLimitDb(bool autoGainProtection, double ceilingDb) =>
        autoGainProtection ? FaderRangeDb + Math.Clamp(ceilingDb, -6.0, 0.0) : UnprotectedBoostLimitDb;

    public static SignalState FromBoost(double boostDb, double limitDb) => FromHeadroom(limitDb - boostDb);

    public static SignalState FromHeadroom(double headroomDb) => headroomDb switch
    {
        <= DangerMarginDb => SignalState.Danger,
        <= WarnMarginDb => SignalState.Warn,
        _ => SignalState.Safe,
    };
}
