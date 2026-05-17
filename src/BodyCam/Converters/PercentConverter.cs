using System.Globalization;

namespace BodyCam.Converters;

/// <summary>
/// Converts an integer percentage (0-100) to a double (0.0-1.0) for ProgressBar.
/// </summary>
public class PercentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int pct ? pct / 100.0 : 0.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double d ? (int)(d * 100) : 0;
}
