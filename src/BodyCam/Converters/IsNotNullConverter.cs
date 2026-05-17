using System.Globalization;

namespace BodyCam.Converters;

/// <summary>
/// Converts a value to true if non-null, false if null.
/// Useful for IsVisible bindings where you want to show UI elements only when a value exists.
/// </summary>
public class IsNotNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("IsNotNullConverter is one-way only.");
}
