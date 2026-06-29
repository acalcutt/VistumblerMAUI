namespace Vistumbler.Core.Models;

public class SignalHistory
{
    public int Id { get; set; }
    public int ApId { get; set; }
    public int Signal { get; set; }
    public int? Rssi { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateTime Timestamp { get; set; }
}
