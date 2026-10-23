using System.Globalization;
using System.Windows.Data;

namespace AudioTunePro.App.ViewModels;

/// <summary>
/// Opacity of the soft glow behind a fader cap, from the cap's width. The design system asks for a glow "at low
/// opacity so it never competes with neighboring bands": that holds at 10 and 15 bands, but the 31-band caps sit
/// only a few pixels apart, so their glows are turned down there to keep each cap readable.
/// </summary>
public sealed class FaderGlowOpacityConverter : IValueConverter
{
    public static readonly FaderGlowOpacityConverter Instance = new();

    private const double NarrowCapWidth = 20;
    private const double NormalOpacity = 0.5;
    private const double NarrowOpacity = 0.2;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double width && width < NarrowCapWidth ? NarrowOpacity : NormalOpacity;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
