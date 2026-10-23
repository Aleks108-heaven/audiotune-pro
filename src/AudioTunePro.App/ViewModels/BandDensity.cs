using System.Windows;
using AudioTunePro.Core.Models;

namespace AudioTunePro.App.ViewModels;

/// <summary>
/// How the fader row is laid out for a given number of bands. The panel is about 600 px wide at the default
/// window size, so the layouts trade readout size for count: 10 bands get full-size readouts, 15 get smaller
/// type, and 31 stand their readouts on end so every band still shows its own dB value and frequency.
/// </summary>
internal static class BandDensity
{
    /// <summary>Height of the value row above and the frequency row below the faders (fixed, so the dB scale lines up).</summary>
    private const double NormalReadoutRowHeight = 26;
    private const double DenseReadoutRowHeight = 44;

    /// <summary>15 bands and up: smaller readout type and narrower faders.</summary>
    public static bool IsCompact(int bandCount) => bandCount >= BandLayouts.TwoThirdsOctave;

    /// <summary>31 bands: readouts are rotated to stand on end beside a very narrow fader.</summary>
    public static bool IsDense(int bandCount) => bandCount >= BandLayouts.ThirdOctave;

    public static GridLength ReadoutRowHeight(int bandCount) =>
        new(IsDense(bandCount) ? DenseReadoutRowHeight : NormalReadoutRowHeight);

    /// <summary>Width of the fader cap (and of its 0 dB tick); the slider is exactly this wide.</summary>
    public static double FaderWidth(int bandCount) => IsDense(bandCount) ? 14 : IsCompact(bandCount) ? 22 : 28;

    public static Thickness ColumnMargin(int bandCount) =>
        IsDense(bandCount) ? new Thickness(0) : IsCompact(bandCount) ? new Thickness(1, 0, 1, 0) : new Thickness(2, 0, 2, 0);
}
