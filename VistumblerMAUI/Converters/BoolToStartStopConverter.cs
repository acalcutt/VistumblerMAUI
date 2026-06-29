using System.Globalization;

namespace VistumblerMAUI.Converters;

/// <summary>Returns "Stop" when scanning (true), "Start" when not scanning (false).</summary>
public class BoolToStartStopConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "Stop" : "Start";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
