namespace VistumblerMAUI.ViewModels;

/// <summary>
/// Normalized view of whatever map feature (live scan AP, daily/weekly/monthly/etc.
/// history tile, or cell tower) was tapped — built from the GeoJSON properties
/// returned by IMapLibreMapController.QueryRenderedFeaturesAtPoint, merged with a
/// local-database lookup by BSSID/MAC when one exists.
/// </summary>
public class MapFeatureInfo
{
    public string SourceLabel { get; set; } = string.Empty; // e.g. "Live scan", "Weekly", "Cells"
    public string Bssid { get; set; } = string.Empty;
    public string Ssid { get; set; } = string.Empty;
    public string Authentication { get; set; } = string.Empty;
    public string Encryption { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string RadioType { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Signal { get; set; } = string.Empty;
    public string Rssi { get; set; } = string.Empty;
    public string FirstSeen { get; set; } = string.Empty;
    public string LastSeen { get; set; } = string.Empty;
    public string UploadedBy { get; set; } = string.Empty;

    /// <summary>True when this BSSID also exists in the local on-device database
    /// (so "View in AP List" can navigate to it).</summary>
    public bool HasLocalRecord { get; set; }

    public string Title => string.IsNullOrWhiteSpace(Ssid) ? "(hidden network)" : Ssid;
}
