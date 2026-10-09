using CommunityToolkit.Mvvm.ComponentModel;

namespace Vistumbler.Core.Models;

/// <summary>
/// The kinds of non-Wi-Fi network, named as WiGLE's CSV Type column names them (and as WifiDB stores them).
/// </summary>
public enum RadioNetworkType
{
    BT,     // Bluetooth classic
    BLE,    // Bluetooth Low Energy
    GSM,
    CDMA,
    WCDMA,
    LTE,
    NR,     // 5G
}

/// <summary>
/// A cell tower or Bluetooth device, kept apart from the Wi-Fi APs (as WiGLE and WifiDB keep them). The fields
/// follow WiGLE's model so a WiGLE CSV round-trips:
/// <list type="bullet">
/// <item>Cells: <see cref="Key"/> is "mccmnc_lac_cid" (TAC for LTE/NR; "sid_nid_bid" for CDMA), <see cref="Name"/> the
/// operator, <see cref="Capabilities"/> "LTE;mccmnc", and <see cref="Channel"/> the ARFCN/UARFCN/EARFCN/NRARFCN.</item>
/// <item>Bluetooth: <see cref="Key"/> is the device's MAC, <see cref="Name"/> its name, <see cref="Capabilities"/> its
/// device class ("Headphones", "Smartphone"…), <see cref="Frequency"/> the device class code, and
/// <see cref="MfgrId"/> a BLE advertisement's manufacturer id.</item>
/// </list>
/// </summary>
public partial class RadioNetwork : ObservableObject
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public RadioNetworkType Type { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CapabilitiesText))]
    private string _capabilities = string.Empty;

    [ObservableProperty] private string _manufacturer = string.Empty;

    public int? Channel { get; set; }
    public int Frequency { get; set; }
    public int? MfgrId { get; set; }

    [ObservableProperty] private int _rssi;
    [ObservableProperty] private int _highestRssi = int.MinValue;

    public DateTime FirstSeen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastSeenText))]
    private DateTime _lastSeen;

    /// <summary>Where it was strongest.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGps))]
    private double? _latitude;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGps))]
    private double? _longitude;

    [ObservableProperty] private bool _isActive;

    /// <summary>Readings, when loaded (exports); not kept for the live list.</summary>
    public List<RadioReading> History { get; set; } = new();

    public bool HasGps => Latitude.HasValue && Longitude.HasValue;
    public bool IsBluetooth => Type is RadioNetworkType.BT or RadioNetworkType.BLE;
    public bool IsCell => !IsBluetooth;

    public string TypeText => Type switch
    {
        RadioNetworkType.BT => "Bluetooth",
        RadioNetworkType.BLE => "Bluetooth LE",
        RadioNetworkType.NR => "5G NR",
        _ => Type.ToString(),
    };

    public string DisplayName => string.IsNullOrEmpty(Name) ? (IsBluetooth ? "(no name)" : Key) : Name;
    public string LastSeenText => LastSeen == default ? "—" : LastSeen.ToLocalTime().ToString("g");

    /// <summary>What WifiDB and WiGLE list as the capabilities, without the trailing ";code" part.</summary>
    public string CapabilitiesText
    {
        get
        {
            int semicolon = Capabilities.LastIndexOf(';');
            return semicolon > 0 ? Capabilities[..semicolon] : Capabilities;
        }
    }
}

/// <summary>One reading of a <see cref="RadioNetwork"/>.</summary>
public class RadioReading
{
    public int Id { get; set; }
    public int NetworkId { get; set; }
    public int GpsId { get; set; }
    public int Rssi { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? Altitude { get; set; }
    public double? Accuracy { get; set; }
    public DateTime Timestamp { get; set; }
}
