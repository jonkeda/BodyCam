using System.Globalization;

namespace BodyCam.Converters;

/// <summary>
/// Converts a boolean to a color: true -> Green, false -> Red.
/// Useful for status indicators.
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Colors.Green : Colors.Red;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("BoolToColorConverter is one-way only.");
}
