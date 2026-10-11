namespace Vistumbler.Core.Models;

/// <summary>
/// One Save &amp; Clear of the session: the file written, how many APs went into it, and the file's MD5, which is
/// how WifiDB knows an upload, so its import can still be found after the local file is deleted.
/// </summary>
public class SessionSave
{
    public int      Id        { get; set; }
    public DateTime SavedUtc  { get; set; }
    public string   FileName  { get; set; } = string.Empty;
    /// <summary>Where the file was written: a path, or a content:// URI in a folder picked on Android.</summary>
    public string   Path      { get; set; } = string.Empty;
    public int      ApCount   { get; set; }
    /// <summary>Lower-case MD5 of the file, or empty when it couldn't be read back.</summary>
    public string   Md5       { get; set; } = string.Empty;
    /// <summary>Whether this save was queued for WifiDB ("Upload each save to WifiDB").</summary>
    public bool     ToWifiDb  { get; set; }
}

/// <summary>
/// Where an AP that Save &amp; Clear removed from the list was strongest, kept so the map can still show the
/// session's earlier APs ("Saved this session"). Only what the map needs; the full history went into the file.
/// </summary>
public class SavedPoint
{
    public int    SaveId         { get; set; }
    public string Bssid          { get; set; } = string.Empty;
    public string Ssid           { get; set; } = string.Empty;
    public string Authentication { get; set; } = string.Empty;
    public string Encryption     { get; set; } = string.Empty;
    public string RadioType      { get; set; } = string.Empty;
    public string Manufacturer   { get; set; } = string.Empty;
    public int    Channel        { get; set; }
    /// <summary>1 open, 2 WEP, 3 secure, as the map colours them.</summary>
    public int    SecurityType   { get; set; }
    public int?   Signal         { get; set; }
    public int?   Rssi           { get; set; }
    public double Latitude       { get; set; }
    public double Longitude      { get; set; }
    public DateTime LastSeen     { get; set; }
}
