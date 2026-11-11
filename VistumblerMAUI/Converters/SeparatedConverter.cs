using System.Globalization;

namespace VistumblerMAUI.Converters;

/// <summary>
/// " · text" for a second item on a line (the manufacturer after the BSSID, the encryption after the
/// authentication), or nothing when there's no text, so an empty one doesn't leave a dangling separator.
/// </summary>
public class SeparatedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrWhiteSpace(s) ? " · " + s : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
