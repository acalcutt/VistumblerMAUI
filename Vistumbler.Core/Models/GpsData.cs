namespace Vistumbler.Core.Models;

/// <summary>
/// GPS position data — sourced from MAUI Geolocation or parsed NMEA sentences.
/// </summary>
public class GpsData
{
    public int GpsId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? Altitude { get; set; }
    public int NumberOfSatellites { get; set; }
    public double? HorizontalDilution { get; set; }
    public double? SpeedKnots { get; set; }
    public double? TrackAngle { get; set; }
    public DateTime Timestamp { get; set; }
    public GpsQuality Quality { get; set; }

    public double SpeedMph  => SpeedKnots.HasValue ? SpeedKnots.Value * 1.15078 : 0;
    public double SpeedKmh  => SpeedKnots.HasValue ? SpeedKnots.Value * 1.852   : 0;
}

public enum GpsQuality
{
    Invalid = 0,
    GpsFix = 1,
    DifferentialGpsFix = 2
}
