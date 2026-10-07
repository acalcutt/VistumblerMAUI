namespace VistumblerMAUI.Services;

/// <summary>Where GPS fixes come from.</summary>
public enum GpsSource
{
    DeviceLocation,   // The OS location service: Windows Location, or the phone's own GPS on Android
    SerialNmea,       // NMEA sentences from a serial COM port (Windows)
    BluetoothNmea,    // NMEA sentences from a paired Bluetooth GPS receiver (Android)
    UsbNmea,          // NMEA sentences from a USB GPS receiver's serial chip (Android)
}

/// <summary>
/// Persisted GPS source configuration (MAUI <see cref="Preferences"/>), mirroring VistumblerCS's choice
/// between the OS location service and an external NMEA receiver. Which external receivers are offered
/// depends on the platform; see <see cref="AvailableSources"/>.
/// </summary>
public static class GpsSettings
{
    private const string SourceKey        = "Gps_Source";
    private const string PortKey          = "Gps_ComPort";
    private const string BaudKey          = "Gps_BaudRate";
    private const string UsbBaudKey       = "Gps_UsbBaudRate";
    private const string ReconnectKey     = "Gps_ReconnectWhenNoData";
    private const string ResetKey         = "Gps_ResetPositionWhenNoFix";
    private const string BluetoothKey     = "Gps_BluetoothAddress";
    private const string BluetoothNameKey = "Gps_BluetoothName";

    /// <summary>Common NMEA serial baud rates for the settings picker.</summary>
    public static readonly int[] BaudRates = { 4800, 9600, 19200, 38400, 57600, 115200 };

    /// <summary>The GPS sources this platform supports, with the names Settings shows for them.</summary>
    public static IReadOnlyList<(GpsSource Source, string Name)> AvailableSources { get; } =
        OperatingSystem.IsWindows() ? new[]
        {
            (GpsSource.DeviceLocation, "Windows Location"),
            (GpsSource.SerialNmea,     "Serial NMEA (COM port)"),
        }
        : OperatingSystem.IsAndroid() ? new[]
        {
            (GpsSource.DeviceLocation, "Phone GPS"),
            (GpsSource.BluetoothNmea,  "Bluetooth GPS receiver (NMEA)"),
            (GpsSource.UsbNmea,        "USB GPS receiver (NMEA)"),
        }
        : new[] { (GpsSource.DeviceLocation, "Device location") };

    /// <summary>The chosen source; one this platform doesn't offer (e.g. a value from another build) reads as DeviceLocation.</summary>
    public static GpsSource Source
    {
        get
        {
            // Older builds stored the device source as "WindowsLocation"; anything unrecognised is the device too
            var stored = Enum.TryParse<GpsSource>(Preferences.Get(SourceKey, ""), out var s) ? s : GpsSource.DeviceLocation;
            return AvailableSources.Any(a => a.Source == stored) ? stored : GpsSource.DeviceLocation;
        }
        set => Preferences.Set(SourceKey, value.ToString());
    }

    public static string ComPort
    {
        get => Preferences.Get(PortKey, string.Empty);
        set => Preferences.Set(PortKey, value ?? string.Empty);
    }

    public static int BaudRate
    {
        get => Preferences.Get(BaudKey, 9600);
        set => Preferences.Set(BaudKey, value);
    }

    /// <summary>Baud rate for a USB receiver on Android. GlobalSat's PL2303 receivers (BU-353) run at 4800.</summary>
    public static int UsbBaudRate
    {
        get => Preferences.Get(UsbBaudKey, 4800);
        set => Preferences.Set(UsbBaudKey, value);
    }

    /// <summary>How long an external receiver may send nothing at all before <see cref="ReconnectWhenNoData"/> acts.</summary>
    public static readonly TimeSpan NoDataTimeout = TimeSpan.FromSeconds(10);

    /// <summary>How long an external receiver may go without a fix before <see cref="ResetPositionWhenNoFix"/> acts.</summary>
    public static readonly TimeSpan NoFixTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The original's "Disconnect GPS when no data is received in over 10 seconds": drop the connection to an
    /// external receiver that has gone silent. Here it then reconnects rather than turning GPS off. Off for
    /// receivers that pause their output.
    /// </summary>
    public static bool ReconnectWhenNoData
    {
        get => Preferences.Get(ReconnectKey, true);
        set => Preferences.Set(ReconnectKey, value);
    }

    /// <summary>
    /// The original's "Reset GPS position when no GPGGA data is received in over 30 seconds": stop stamping APs
    /// with an external receiver's last position once it has sent no fix for <see cref="NoFixTimeout"/>.
    /// </summary>
    public static bool ResetPositionWhenNoFix
    {
        get => Preferences.Get(ResetKey, true);
        set => Preferences.Set(ResetKey, value);
    }

    /// <summary>Whether the chosen source is an external NMEA receiver, which the two options above apply to.</summary>
    public static bool IsExternalReceiver => Source is GpsSource.SerialNmea or GpsSource.BluetoothNmea or GpsSource.UsbNmea;

    /// <summary>MAC address of the paired Bluetooth GPS receiver to read from.</summary>
    public static string BluetoothAddress
    {
        get => Preferences.Get(BluetoothKey, string.Empty);
        set => Preferences.Set(BluetoothKey, value ?? string.Empty);
    }

    /// <summary>Its name when it was chosen, to show in Settings and messages.</summary>
    public static string BluetoothName
    {
        get => Preferences.Get(BluetoothNameKey, string.Empty);
        set => Preferences.Set(BluetoothNameKey, value ?? string.Empty);
    }

    /// <summary>Serial ports available on this device (empty off Windows).</summary>
    public static string[] AvailablePorts()
    {
#if WINDOWS
        try { return System.IO.Ports.SerialPort.GetPortNames(); }
        catch { return Array.Empty<string>(); }
#else
        return Array.Empty<string>();
#endif
    }
}
