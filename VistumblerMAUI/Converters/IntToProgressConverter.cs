using System.Globalization;

namespace VistumblerMAUI.Converters;

/// <summary>Converts an int signal percentage (0–100) to a double 0.0–1.0 for ProgressBar.</summary>
public class IntToProgressConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int i ? Math.Clamp(i / 100.0, 0.0, 1.0) : 0.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
