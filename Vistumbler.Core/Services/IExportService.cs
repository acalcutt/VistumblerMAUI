using Vistumbler.Core.Models;

namespace Vistumbler.Core.Services;

/// <summary>
/// Service for exporting access point data to various formats.
/// </summary>
public interface IExportService
{
    /// <summary>
    /// Export access points to KML format for Google Earth. When <see cref="ExportOptions.ShowTrack"/>
    /// is set, <paramref name="gpsFixes"/> is drawn as a GPS track line (split on long time gaps).
    /// </summary>
    Task ExportToKmlAsync(string filePath, List<AccessPoint> accessPoints, ExportOptions options, List<GpsData> gpsFixes);

    /// <summary>
    /// Export access points to GPX format, including a GPS track from <paramref name="gpsFixes"/>.
    /// </summary>
    Task ExportToGpxAsync(string filePath, List<AccessPoint> accessPoints, List<GpsData> gpsFixes);

    /// <summary>
    /// Export access points to NS1 format (NetStumbler binary)
    /// </summary>
    Task ExportToNs1Async(string filePath, List<AccessPoint> accessPoints);

    /// <summary>
    /// Export access points to KismetDB format
    /// </summary>
    Task ExportToKismetDbAsync(string filePath, List<AccessPoint> accessPoints);

    /// <summary>
    /// Export access points to NetXML format (Kismet Legacy)
    /// </summary>
    Task ExportToNetXmlAsync(string filePath, List<AccessPoint> accessPoints);

    /// <summary>
    /// Export to the Vistumbler Detailed CSV format — one row per signal observation, with
    /// the GPS fix details from <paramref name="gpsFixes"/> (matches the au3 _ExportToCSV Detailed=1).
    /// </summary>
    Task ExportToCsvAsync(string filePath, List<AccessPoint> accessPoints, List<GpsData> gpsFixes);

    /// <summary>
    /// Export access points to WiGLE CSV format
    /// </summary>
    Task ExportToWigleCsvAsync(string filePath, List<AccessPoint> accessPoints);

    /// <summary>
    /// Export to VS1 text format (Vistumbler native, Detailed Export v4). Each AP must have
    /// its SignalHistory populated (with GpsId), and <paramref name="gpsFixes"/> must contain
    /// every GPS fix the history references, so the file round-trips into official Vistumbler.
    /// </summary>
    Task ExportToVs1Async(string filePath, List<AccessPoint> accessPoints, List<GpsData> gpsFixes);

    /// <summary>
    /// Export to VSZ (zipped VS1) format.
    /// </summary>
    Task ExportToVszAsync(string filePath, List<AccessPoint> accessPoints, List<GpsData> gpsFixes);
}

public class ExportOptions
{
    public bool IncludeOpenNetworks { get; set; } = true;
    public bool IncludeWepNetworks { get; set; } = true;
    public bool IncludeSecureNetworks { get; set; } = true;
    public bool ShowTrack { get; set; } = true;
    public bool UseSignalColors { get; set; } = true;
    public string TrackColor { get; set; } = "7F0000FF";
}
