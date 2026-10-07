using System.Globalization;
using Vistumbler.Core.Models;

namespace VistumblerMAUI.Services;

/// <summary>
/// Turns NMEA 0183 sentences (GGA and RMC, from any talker: $GP, $GN, $GL, …) into <see cref="GpsData"/>, for
/// the external receivers: a serial COM port on Windows and a Bluetooth receiver on Android.
/// </summary>
public sealed class NmeaParser
{
    private GpsData? _current;

    /// <summary>The latest fix built from the sentences so far, or null before the first fix.</summary>
    public GpsData? Current => _current;

    /// <summary>Reads one sentence; returns the updated fix when it carried a position with a valid fix.</summary>
    public GpsData? Process(string sentence)
    {
        sentence = sentence.Trim();
        if (sentence.Length < 7 || sentence[0] != '$') return null;
        int star = sentence.IndexOf('*');
        if (star > 0) sentence = sentence[..star];   // drop the checksum
        var p = sentence.Split(',');

        try
        {
            if (p[0].EndsWith("GGA")) return ParseGga(p);
            if (p[0].EndsWith("RMC")) return ParseRmc(p);
        }
        catch { /* skip a malformed sentence */ }
        return null;
    }

    private GpsData? ParseGga(string[] p)
    {
        // $..GGA,time,lat,N/S,lon,E/W,quality,sats,hdop,alt,M,...
        if (p.Length < 10) return null;
        _current ??= new GpsData();

        if (!string.IsNullOrEmpty(p[2]) && !string.IsNullOrEmpty(p[3]))
            _current.Latitude = ToDecimalDegrees(p[2], p[3]);
        if (!string.IsNullOrEmpty(p[4]) && !string.IsNullOrEmpty(p[5]))
            _current.Longitude = ToDecimalDegrees(p[4], p[5]);
        if (int.TryParse(p[6], out int q)) _current.Quality = (GpsQuality)q;
        if (int.TryParse(p[7], out int sats)) _current.NumberOfSatellites = sats;
        if (double.TryParse(p[8], NumberStyles.Float, CultureInfo.InvariantCulture, out double hdop)) _current.HorizontalDilution = hdop;
        if (double.TryParse(p[9], NumberStyles.Float, CultureInfo.InvariantCulture, out double alt)) _current.Altitude = alt;

        return Fix();
    }

    private GpsData? ParseRmc(string[] p)
    {
        // $..RMC,time,status,lat,N/S,lon,E/W,speed,track,date,...
        if (p.Length < 10) return null;
        if (p[2] != "A") return null;               // A = valid fix
        _current ??= new GpsData();

        if (!string.IsNullOrEmpty(p[3]) && !string.IsNullOrEmpty(p[4]))
            _current.Latitude = ToDecimalDegrees(p[3], p[4]);
        if (!string.IsNullOrEmpty(p[5]) && !string.IsNullOrEmpty(p[6]))
            _current.Longitude = ToDecimalDegrees(p[5], p[6]);
        if (double.TryParse(p[7], NumberStyles.Float, CultureInfo.InvariantCulture, out double kn)) _current.SpeedKnots = kn;
        if (double.TryParse(p[8], NumberStyles.Float, CultureInfo.InvariantCulture, out double trk)) _current.TrackAngle = trk;
        if (_current.Quality == GpsQuality.Invalid) _current.Quality = GpsQuality.GpsFix;

        return Fix();
    }

    // Only a sentence with a fix counts; until then there is nothing to stamp onto APs
    private GpsData? Fix()
    {
        if (_current is null || _current.Quality == GpsQuality.Invalid) return null;
        _current.Timestamp = DateTime.UtcNow;
        return _current;
    }

    // ddmm.mmmm / dddmm.mmmm + hemisphere → signed decimal degrees.
    private static double ToDecimalDegrees(string coordinate, string direction)
    {
        int dot = coordinate.IndexOf('.');
        if (dot < 3) return 0;
        int degLen = dot - 2;
        if (!double.TryParse(coordinate[..degLen], NumberStyles.Float, CultureInfo.InvariantCulture, out double deg) ||
            !double.TryParse(coordinate[degLen..], NumberStyles.Float, CultureInfo.InvariantCulture, out double min))
            return 0;

        double dd = deg + min / 60.0;
        if (direction is "S" or "W") dd = -dd;
        return dd;
    }
}
