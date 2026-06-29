using System.Globalization;
using VistumblerMAUI.ViewModels;

namespace VistumblerMAUI.Converters;

/// <summary>True when the bound <see cref="ExportFormat"/> is Kml — used to show KML-only export options.</summary>
public class IsKmlFormatConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ExportFormat.Kml;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
