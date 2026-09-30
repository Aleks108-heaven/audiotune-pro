using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AudioTunePro.App.ViewModels;

/// <summary>
/// Splits a full-width meter into "filled" and "empty" star columns from the 0..1 level
/// (ConverterParameter "fill" or "rest"), so the meter fills whatever width its panel gives it.
/// </summary>
public sealed class LevelToGridLengthConverter : IValueConverter
{
    public static readonly LevelToGridLengthConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double level = Math.Clamp(value is float f ? f : value is double d ? d : 0.0, 0.0, 1.0);
        return new GridLength(parameter as string == "rest" ? 1.0 - level : level, GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
