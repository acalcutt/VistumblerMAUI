using System.Globalization;

namespace VistumblerMAUI.Converters;

/// <summary>
/// Converts a channel frequency in MHz to a short Wi-Fi band label
/// ("2.4 GHz" / "5 GHz" / "6 GHz"), or an empty string when unknown.
/// Used for the compact per-AP detail line on the Scan page.
/// </summary>
public class FrequencyToBandConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int mhz = value switch
        {
            int i    => i,
            long l   => (int)l,
            double d => (int)d,
            _        => 0,
        };
        return mhz switch
        {
            >= 5925 => "6 GHz",
            >= 4900 => "5 GHz",
            >= 2400 => "2.4 GHz",
            _       => string.Empty,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
