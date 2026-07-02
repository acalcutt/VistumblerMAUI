using CommunityToolkit.Mvvm.ComponentModel;
using Vistumbler.Core.Extensions;

namespace Vistumbler.Core.Models;

/// <summary>
/// Represents a detected WiFi access point.
///
/// Implements change notification (via <see cref="ObservableObject"/>) so the Scan
/// page's CollectionView can update rows in place when the scan loop mutates an
/// existing AP — instead of the list being rebuilt each cycle, which reset scroll.
/// Properties the scan loop updates or the list binds to raise PropertyChanged; the
/// rest stay plain. The DB layer maps to/from separate row DTOs, so this base class
/// does not affect persistence.
/// </summary>
public partial class AccessPoint : ObservableObject
{
    public int ApId { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OuiPrefix))]
    private string _bssid = string.Empty;

    [ObservableProperty] private string _ssid = string.Empty;
    [ObservableProperty] private string _manufacturer = string.Empty;

    public string Label { get; set; } = string.Empty;
    public NetworkType NetworkType { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AuthText))]
    private AuthenticationType _authentication;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EncryptionText))]
    private EncryptionType _encryption;

    public string RadioType { get; set; } = string.Empty;

    [ObservableProperty] private int _channel;

    public int FrequencyMhz { get; set; }

    [ObservableProperty] private int? _signal;
    [ObservableProperty] private int? _highestSignal;
    [ObservableProperty] private int? _rssi;

    public int? HighestRssi { get; set; }
    public string BasicTransferRates { get; set; } = string.Empty;
    public string OtherTransferRates { get; set; } = string.Empty;
    public DateTime FirstSeen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastSeenText))]
    private DateTime _lastSeen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGps))]
    [NotifyPropertyChangedFor(nameof(GpsText))]
    private double? _latitude;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGps))]
    [NotifyPropertyChangedFor(nameof(GpsText))]
    private double? _longitude;

    [ObservableProperty] private bool _isActive;

    public List<SignalHistory> SignalHistory { get; set; } = new();

    // ── History links (VistumblerMDB-style: AP references HIST rows by id) ──────
    // The DB tracks first/last activity and the high-signal / high-RSSI samples by
    // storing the SignalHistory row id here rather than the raw values; the values
    // above (FirstSeen/LastSeen/Signal/HighestSignal/…) are resolved from these rows
    // (and their linked GPS fix) when an AP is loaded. 0 = not set.
    public int FirstHistId      { get; set; }
    public int LastHistId       { get; set; }
    public int HighSignalHistId { get; set; }
    public int HighRssiHistId   { get; set; }

    /// <summary>Normalised 6-hex-char OUI prefix used for manufacturer lookup.</summary>
    public string OuiPrefix =>
        System.Text.RegularExpressions.Regex.Replace(Bssid, "[^0-9A-Fa-f]", "")
            .PadRight(6)[..6].ToUpperInvariant();

    // ── Display helpers (used by the Scan list / AP details) ──────────────────
    /// <summary>First-seen as local date + time ("—" when unset).</summary>
    public string FirstSeenText => FirstSeen == default ? "—" : FirstSeen.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>Last-seen as local date + time ("—" when unset). Updates as the AP is re-seen.</summary>
    public string LastSeenText => LastSeen == default ? "—" : LastSeen.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>True when this AP has a GPS position stamped.</summary>
    public bool HasGps => Latitude.HasValue && Longitude.HasValue;

    /// <summary>GPS position as "lat, lon" (5 dp), or empty when unknown.</summary>
    public string GpsText => HasGps
        ? $"{Latitude!.Value:F5}, {Longitude!.Value:F5}"
        : string.Empty;

    /// <summary>Friendly authentication name (Vistumbler-style, e.g. "WPA2-Personal").</summary>
    public string AuthText => Authentication.ToLegacyString();

    /// <summary>Friendly encryption name (e.g. "CCMP", "WEP", "None").</summary>
    public string EncryptionText => Encryption.ToLegacyString();
}
