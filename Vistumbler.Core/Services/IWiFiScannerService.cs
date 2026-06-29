using Vistumbler.Core.Models;

namespace Vistumbler.Core.Services;

/// <summary>
/// Scans for nearby WiFi access points.
/// On Android: WifiManager.startScan() + SCAN_RESULTS_AVAILABLE_ACTION broadcast.
/// On Windows: ManagedNativeWifi / Wlan API.
/// On iOS/macOS: NEHotspotNetwork (limited — current SSID/BSSID only, no full scan).
/// </summary>
public interface IWiFiScannerService
{
    /// <summary>Raised on each scan cycle with all currently visible APs.</summary>
    event EventHandler<AccessPointsDetectedEventArgs>? AccessPointsDetected;

    /// <summary>Raised when scanning encounters a hardware or permission error.</summary>
    event EventHandler<ScanErrorEventArgs>? ScanError;

    bool IsScanning { get; }

    /// <summary>Delay between scan requests in milliseconds (default 2000).</summary>
    int ScanIntervalMs { get; set; }

    Task StartScanningAsync(CancellationToken cancellationToken = default);
    void StopScanning();

    Task<List<WiFiAdapter>> GetAvailableAdaptersAsync();
    void SetActiveAdapter(string adapterId);

    /// <summary>Returns the BSSID of the currently associated AP, or null.</summary>
    Task<string?> GetConnectedBssidAsync();
}

public class AccessPointsDetectedEventArgs : EventArgs
{
    public List<AccessPoint> AccessPoints { get; set; } = new();
}

public class ScanErrorEventArgs : EventArgs
{
    public string ErrorMessage { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
}
