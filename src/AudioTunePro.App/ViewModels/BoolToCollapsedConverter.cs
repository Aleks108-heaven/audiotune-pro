using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AudioTunePro.App.ViewModels;

/// <summary>True -> Collapsed, False -> Visible. Used to hide the "Get Equalizer APO" button once it's installed.</summary>
public sealed class BoolToCollapsedConverter : IValueConverter
{
    public static readonly BoolToCollapsedConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
