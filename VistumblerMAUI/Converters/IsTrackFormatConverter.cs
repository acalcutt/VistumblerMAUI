using System.Globalization;
using VistumblerMAUI.ViewModels;

namespace VistumblerMAUI.Converters;

/// <summary>True when the bound <see cref="ExportFormat"/> can embed a GPS track
/// (KML LineString / GPX &lt;trk&gt;) — used to show the "Include GPS track" option.</summary>
public class IsTrackFormatConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ExportFormat.Kml or ExportFormat.Gpx;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
