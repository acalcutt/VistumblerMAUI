using System.Globalization;

namespace Vistumbler.Core.Models;

/// <summary>How coordinates are shown, after the original Vistumbler's GPS Format setting.</summary>
public enum GpsDisplayFormat
{
    Decimal,                // 48.11730, -11.51667
    DecimalHemisphere,      // N 48.1173000, W 11.5166667  (the original's "dd.dddd")
    DegreesMinutes,         // N 4807.0380, W 01131.0000   (the original's "ddmm.mmmm", as NMEA sends it)
    DegreesMinutesSeconds,  // N 48° 7' 2.28", W 11° 31' 0.00"  (the original's "dd mm ss")
}

/// <summary>Formats coordinates for display in the format chosen in Settings → GPS.</summary>
public static class GpsFormatter
{
    /// <summary>The format in use; set from the app's settings at startup and when changed.</summary>
    public static GpsDisplayFormat Format { get; set; } = GpsDisplayFormat.Decimal;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string ToText(double latitude, double longitude) => ToText(latitude, longitude, Format);

    public static string ToText(double latitude, double longitude, GpsDisplayFormat format) => format switch
    {
        GpsDisplayFormat.DecimalHemisphere =>
            $"{Hemisphere(latitude, 'N', 'S')} {Math.Abs(latitude).ToString("F7", Inv)}, " +
            $"{Hemisphere(longitude, 'E', 'W')} {Math.Abs(longitude).ToString("F7", Inv)}",
        GpsDisplayFormat.DegreesMinutes =>
            $"{Hemisphere(latitude, 'N', 'S')} {DegreesMinutes(latitude, 2)}, " +
            $"{Hemisphere(longitude, 'E', 'W')} {DegreesMinutes(longitude, 3)}",
        GpsDisplayFormat.DegreesMinutesSeconds =>
            $"{Hemisphere(latitude, 'N', 'S')} {DegreesMinutesSeconds(latitude)}, " +
            $"{Hemisphere(longitude, 'E', 'W')} {DegreesMinutesSeconds(longitude)}",
        _ => $"{latitude.ToString("F5", Inv)}, {longitude.ToString("F5", Inv)}",
    };

    private static char Hemisphere(double value, char positive, char negative) => value < 0 ? negative : positive;

    // NMEA style: degrees padded to 2 (lat) or 3 (lon) digits, then minutes to 4 decimals, e.g. 4807.0380
    private static string DegreesMinutes(double value, int degreeDigits)
    {
        value = Math.Abs(value);
        int degrees = (int)value;
        double minutes = Math.Round((value - degrees) * 60, 4);
        if (minutes >= 60) { degrees++; minutes = 0; }
        return degrees.ToString(new string('0', degreeDigits), Inv) + minutes.ToString("00.0000", Inv);
    }

    private static string DegreesMinutesSeconds(double value)
    {
        value = Math.Abs(value);
        int degrees = (int)value;
        double totalMinutes = (value - degrees) * 60;
        int minutes = (int)totalMinutes;
        double seconds = Math.Round((totalMinutes - minutes) * 60, 2);
        if (seconds >= 60) { minutes++; seconds = 0; }
        if (minutes >= 60) { degrees++; minutes = 0; }
        return $"{degrees}° {minutes}' {seconds.ToString("0.00", Inv)}\"";
    }
}
