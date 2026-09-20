namespace AudioTunePro.App.ViewModels;

/// <summary>
/// The three gain-risk states shared by EQ faders, the Limiter's PeakIndicator and (in spirit)
/// the level meter, per design-system/components.md: signal-safe / signal-warn / signal-danger.
/// </summary>
public enum SignalState
{
    Safe,
    Warn,
    Danger,
}

/// <summary>
/// Turns "how much headroom is left below the limiter ceiling" into one of the three
/// <see cref="SignalState"/> values, so every control that reads gain risk agrees.
/// The design system specifies the three states but not their exact margins; 3 dB / 1 dB
/// were chosen as a reasonable caution / final-margin split for this app's +/-12 dB tone range.
/// </summary>
public static class SignalStateCalculator
{
    private const double WarnMarginDb = 3.0;
    private const double DangerMarginDb = 1.0;

    public static SignalState FromHeadroom(double headroomDb) => headroomDb switch
    {
        <= DangerMarginDb => SignalState.Danger,
        <= WarnMarginDb => SignalState.Warn,
        _ => SignalState.Safe,
    };
}
