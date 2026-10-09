using Vistumbler.Core.Models;

namespace VistumblerMAUI.Services;

/// <summary>
/// Scanning and display settings from the original Vistumbler: Auto Scan APs on launch, the Wi-Fi adapter
/// (Interface menu) and the GPS coordinate format. Backed by MAUI <see cref="Preferences"/>.
/// </summary>
public static class ScanSettings
{
    private const string ScanOnLaunchKey = "Scan_OnLaunch";
    private const string GpsOnLaunchKey  = "Gps_OnLaunch";
    private const string AdapterKey      = "Scan_AdapterId";
    private const string GpsFormatKey    = "Gps_DisplayFormat";
    private const string SaveGpsKey      = "Gps_SaveWithoutAps";
    private const string CellsKey        = "Scan_Cells";
    private const string BluetoothKey    = "Scan_Bluetooth";

    /// <summary>Raised when cell or Bluetooth scanning is turned on or off, so a running scan can follow.</summary>
    public static event EventHandler? RadioChanged;

    /// <summary>Also record cell towers while scanning (Android). Off by default.</summary>
    public static bool ScanCells
    {
        get => Preferences.Get(CellsKey, false);
        set { Preferences.Set(CellsKey, value); RadioChanged?.Invoke(null, EventArgs.Empty); }
    }

    /// <summary>Also record Bluetooth and Bluetooth LE devices while scanning (Android). Off by default.</summary>
    public static bool ScanBluetooth
    {
        get => Preferences.Get(BluetoothKey, false);
        set { Preferences.Set(BluetoothKey, value); RadioChanged?.Invoke(null, EventArgs.Empty); }
    }

    /// <summary>Start scanning for APs as soon as the app opens (after a session is chosen).</summary>
    public static bool ScanOnLaunch
    {
        get => Preferences.Get(ScanOnLaunchKey, false);
        set => Preferences.Set(ScanOnLaunchKey, value);
    }

    /// <summary>Turn GPS on as soon as the app opens.</summary>
    public static bool GpsOnLaunch
    {
        get => Preferences.Get(GpsOnLaunchKey, false);
        set => Preferences.Set(GpsOnLaunchKey, value);
    }

    /// <summary>The Wi-Fi adapter to scan with on Windows; empty scans with all of them.</summary>
    public static string AdapterId
    {
        get => Preferences.Get(AdapterKey, string.Empty);
        set => Preferences.Set(AdapterKey, value ?? string.Empty);
    }

    /// <summary>
    /// The original's "Save all GPS data" (on by default there too): record GPS points even when a scan finds
    /// no APs, and while GPS is on without scanning, so exported tracks have no gaps.
    /// </summary>
    public static bool SaveGpsWithoutAps
    {
        get => Preferences.Get(SaveGpsKey, true);
        set => Preferences.Set(SaveGpsKey, value);
    }

    public static GpsDisplayFormat GpsFormat
    {
        get => (GpsDisplayFormat)Preferences.Get(GpsFormatKey, (int)GpsDisplayFormat.Decimal);
        set
        {
            Preferences.Set(GpsFormatKey, (int)value);
            GpsFormatter.Format = value;
        }
    }

    /// <summary>Applies the saved settings that other code reads directly. Call once at startup.</summary>
    public static void Apply(Vistumbler.Core.Services.IWiFiScannerService wifi)
    {
        GpsFormatter.Format = GpsFormat;
        wifi.SetActiveAdapter(AdapterId);
    }
}
