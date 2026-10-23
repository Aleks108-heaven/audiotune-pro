namespace AudioTunePro.Core.Models;

/// <summary>
/// The graphic-EQ resolutions the app offers: the original 10 octave bands, 15 two-thirds-octave bands
/// and 31 third-octave bands (the pro-audio standard). All of them are rendered as one Equalizer APO
/// <c>GraphicEQ</c> line, so more bands means a finer curve, not more filters running.
/// </summary>
public static class BandLayouts
{
    public const int Octave = 10;
    public const int TwoThirdsOctave = 15;
    public const int ThirdOctave = 31;

    /// <summary>Resolution used on first run and when upgrading from the fixed 10-band build.</summary>
    public const int DefaultCount = TwoThirdsOctave;

    public static IReadOnlyList<int> SupportedCounts { get; } = new[] { Octave, TwoThirdsOctave, ThirdOctave };

    // The 10-band set is the app's original one (31 and 62 rather than the ISO 31.5 and 63), kept so existing
    // presets and saved state map onto it unchanged.
    private static readonly double[] OctaveHz =
        { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };

    private static readonly double[] TwoThirdsOctaveHz =
        { 25, 40, 63, 100, 160, 250, 400, 630, 1000, 1600, 2500, 4000, 6300, 10000, 16000 };

    private static readonly double[] ThirdOctaveHz =
    {
        20, 25, 31.5, 40, 50, 63, 80, 100, 125, 160, 200, 250, 315, 400, 500, 630, 800,
        1000, 1250, 1600, 2000, 2500, 3150, 4000, 5000, 6300, 8000, 10000, 12500, 16000, 20000,
    };

    public static bool IsSupported(int count) => count is Octave or TwoThirdsOctave or ThirdOctave;

    /// <summary>
    /// The engine to show after the user picks another resolution. A curve that is still exactly the selected preset
    /// is rebuilt from the preset's own definition, so flipping between layouts any number of times never piles up
    /// interpolation error; a curve the user has edited is interpolated onto the new layout (going to fewer bands
    /// smooths detail the coarser layout cannot hold). Always a new object.
    /// </summary>
    /// <param name="live">What is playing now.</param>
    /// <param name="selectedPreset">The preset it was loaded from, or null when there is none.</param>
    public static EqEngine Switch(EqEngine live, EqEngine? selectedPreset, int count)
    {
        bool untouched = selectedPreset is not null && live.IsEquivalentTo(selectedPreset.WithBandCount(live.Bands.Count));
        return untouched ? selectedPreset!.WithBandCount(count) : live.WithBandCount(count);
    }

    /// <summary>Center frequencies (Hz, ascending) for a supported band count. Returns a copy.</summary>
    public static double[] Frequencies(int count) => count switch
    {
        Octave => (double[])OctaveHz.Clone(),
        TwoThirdsOctave => (double[])TwoThirdsOctaveHz.Clone(),
        ThirdOctave => (double[])ThirdOctaveHz.Clone(),
        _ => throw new ArgumentOutOfRangeException(nameof(count), count, "Supported band counts are 10, 15 and 31."),
    };
}
