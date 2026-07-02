using System.Globalization;

namespace VistumblerMAUI.Converters;

/// <summary>
/// Green when a feature is running (true), red when stopped (false) — for the Scan APs /
/// Use GPS toggle buttons, matching the original VistumblerMDB's start/stop coloring.
/// </summary>
public class RunningToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Color.FromArgb("#2E7D32")   // green (running)
                          : Color.FromArgb("#C62828");  // red (stopped)

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
