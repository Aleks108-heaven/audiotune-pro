using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;

namespace AudioTunePro.App.ViewModels;

/// <summary>Maps a <see cref="SignalState"/> to its matching signal-* brush from the design tokens.</summary>
public sealed class SignalStateToBrushConverter : IValueConverter
{
    public static readonly SignalStateToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        BrushFor(value as SignalState? ?? SignalState.Safe);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    internal static SolidColorBrush BrushFor(SignalState state) => (SolidColorBrush)Application.Current.FindResource(state switch
    {
        SignalState.Danger => "signal-danger",
        SignalState.Warn => "signal-warn",
        _ => "signal-safe",
    });
}

/// <summary>A 12%-opacity tint of the matching signal color, for the PeakIndicator pill background.</summary>
public sealed class SignalStateToTintBrushConverter : IValueConverter
{
    public static readonly SignalStateToTintBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var c = SignalStateToBrushConverter.BrushFor(value as SignalState? ?? SignalState.Safe).Color;
        return new SolidColorBrush(Color.FromArgb((byte)Math.Round(0.12 * 255), c.R, c.G, c.B));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>The PeakIndicator's words for each state, per components.md ("the word is what makes this indicator colorblind-safe").</summary>
public sealed class SignalStateToLabelConverter : IValueConverter
{
    public static readonly SignalStateToLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        SignalState.Danger => "Near clipping",
        SignalState.Warn => "Approaching ceiling",
        _ => "Safe",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
