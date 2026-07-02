namespace Vistumbler.Core.Models;

public class SignalHistory
{
    public int Id { get; set; }
    public int ApId { get; set; }

    /// <summary>GPS table row id for the fix taken with this sample (0 = none). The
    /// position is stored in the GPS table and joined in; the Latitude/Longitude below
    /// remain as a fallback for legacy rows that predate the GPS link.</summary>
    public int GpsId { get; set; }

    public int Signal { get; set; }
    public int? Rssi { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateTime Timestamp { get; set; }
}
