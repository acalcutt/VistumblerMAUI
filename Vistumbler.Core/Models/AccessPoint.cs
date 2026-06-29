namespace Vistumbler.Core.Models;

/// <summary>
/// Represents a detected WiFi access point.
/// </summary>
public class AccessPoint
{
    public int ApId { get; set; }
    public string Bssid { get; set; } = string.Empty;
    public string Ssid { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public NetworkType NetworkType { get; set; }
    public AuthenticationType Authentication { get; set; }
    public EncryptionType Encryption { get; set; }
    public string RadioType { get; set; } = string.Empty;
    public int Channel { get; set; }
    public int FrequencyMhz { get; set; }
    public int? Signal { get; set; }
    public int? HighestSignal { get; set; }
    public int? Rssi { get; set; }
    public int? HighestRssi { get; set; }
    public string BasicTransferRates { get; set; } = string.Empty;
    public string OtherTransferRates { get; set; } = string.Empty;
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool IsActive { get; set; }
    public List<SignalHistory> SignalHistory { get; set; } = new();

    /// <summary>Normalised 6-hex-char OUI prefix used for manufacturer lookup.</summary>
    public string OuiPrefix =>
        System.Text.RegularExpressions.Regex.Replace(Bssid, "[^0-9A-Fa-f]", "")
            .PadRight(6)[..6].ToUpperInvariant();
}
