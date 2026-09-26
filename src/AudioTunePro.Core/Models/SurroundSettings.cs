namespace AudioTunePro.Core.Models;

public enum SurroundMode
{
    Off = 0,

    /// <summary>Stereo widening for laptop/desk speakers (mid/side matrix, gain math only).</summary>
    Speakers = 1,

    /// <summary>Headphone 3D: crossfeed by default, or HRTF convolution if an impulse-response file is set.</summary>
    Headphones = 2,

    /// <summary>Picks Speakers or Headphones from the current output device. Resolved by the app before rendering.</summary>
    Auto = 3,
}

public enum OutputKind
{
    Speakers,
    Headphones,
}

/// <summary>
/// 3D surround settings. Both modes are chosen to be as cheap as Equalizer APO allows:
/// Speakers is a 2x2 gain matrix (a handful of multiplies per sample), and Headphones
/// crossfeed is two low-pass biquads plus a sub-millisecond delay. Only the optional
/// HRTF path uses FFT convolution, which costs noticeably more.
/// </summary>
public sealed class SurroundSettings
{
    public SurroundMode Mode { get; set; } = SurroundMode.Auto;

    /// <summary>Effect strength from 0 (none) to 1 (maximum).</summary>
    public double Amount { get; set; } = 0.5;

    /// <summary>
    /// Optional HRTF/binaural impulse-response WAV for Headphones mode. A 2-channel file is
    /// convolved per ear; a 4-channel file is treated by Equalizer APO as a true-stereo IR.
    /// Keep it short (256-512 samples) to keep CPU use low.
    /// </summary>
    public string? HrtfFilePath { get; set; }

    public SurroundSettings Clone() => new()
    {
        Mode = Mode,
        Amount = Amount,
        HrtfFilePath = HrtfFilePath,
    };
}
