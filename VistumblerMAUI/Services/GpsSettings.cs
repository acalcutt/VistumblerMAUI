namespace VistumblerMAUI.Services;

/// <summary>Where GPS fixes come from.</summary>
public enum GpsSource
{
    WindowsLocation,   // MAUI Geolocation (Windows Location API / OS location)
    SerialNmea         // NMEA sentences over a serial COM port (Windows only)
}

/// <summary>
/// Persisted GPS source configuration (MAUI <see cref="Preferences"/>), mirroring
/// VistumblerCS's choice between the Windows Location API and a serial NMEA receiver.
/// </summary>
public static class GpsSettings
{
    private const string SourceKey = "Gps_Source";
    private const string PortKey   = "Gps_ComPort";
    private const string BaudKey   = "Gps_BaudRate";

    /// <summary>Common NMEA serial baud rates for the settings picker.</summary>
    public static readonly int[] BaudRates = { 4800, 9600, 19200, 38400, 57600, 115200 };

    public static GpsSource Source
    {
        get => Preferences.Get(SourceKey, nameof(GpsSource.WindowsLocation)) == nameof(GpsSource.SerialNmea)
            ? GpsSource.SerialNmea : GpsSource.WindowsLocation;
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
