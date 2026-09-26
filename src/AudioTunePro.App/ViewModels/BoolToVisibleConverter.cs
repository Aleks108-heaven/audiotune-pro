using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AudioTunePro.App.ViewModels;

/// <summary>True -> Visible, False -> Collapsed. Used to show the HRTF file row only in Headphones mode.</summary>
public sealed class BoolToVisibleConverter : IValueConverter
{
    public static readonly BoolToVisibleConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
