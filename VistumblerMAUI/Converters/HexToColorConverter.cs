using System.Globalization;

namespace VistumblerMAUI.Converters;

/// <summary>
/// Converts a 6-char hex string (with or without a leading '#') to a <see cref="Color"/>.
/// Returns transparent for invalid input, so a color-swatch preview stays blank while the
/// user is mid-edit. Used by the Map-colors settings table.
/// </summary>
public class HexToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s)
        {
            var hex = s.Trim().TrimStart('#');
            if (hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, null, out _))
                return Color.FromArgb("#" + hex);
        }
        return Colors.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
