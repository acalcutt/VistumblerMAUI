using System.Globalization;

namespace VistumblerMAUI.Converters;

/// <summary>Returns a red Color while scanning, blue when idle.</summary>
public class BoolToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Color.FromArgb("#C62828") : Color.FromArgb("#1565C0");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
