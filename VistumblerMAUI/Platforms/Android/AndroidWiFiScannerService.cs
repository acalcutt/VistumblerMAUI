#if ANDROID
using Android.Content;
using Android.Net.Wifi;
using Android.OS;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;
using AndroidApp = Android.App.Application;

namespace VistumblerMAUI.Platforms.Android;

/// <summary>
/// Android WiFi scanner.
///
/// Mirrors the vistumbler-android WifiReceiver approach:
///   1. Call WifiManager.StartScan() on a background thread (avoids ANR from Binder blocking).
///   2. Register a BroadcastReceiver for WifiManager.ScanResultsAvailableAction.
///   3. Parse ScanResult list — BSSID, SSID, capabilities string, Level (RSSI), Frequency.
///   4. Derive Channel from Frequency using the same band logic as wigle-wifi.
///   5. Parse security type from capabilities string (same pattern matching).
///
/// Requires manifest permissions:
///   ACCESS_FINE_LOCATION, ACCESS_WIFI_STATE, CHANGE_WIFI_STATE
/// Android 10+ requires foreground service with ACCESS_FINE_LOCATION for reliable scanning.
/// </summary>
public class AndroidWiFiScannerService : IWiFiScannerService
{
    private WifiManager?  _wifiManager;
    private ScanReceiver? _receiver;
    private bool          _receiverRegistered;
    private bool          _isScanning;
    private string?       _activeAdapterId;

    public event EventHandler<AccessPointsDetectedEventArgs>? AccessPointsDetected;
    public event EventHandler<ScanErrorEventArgs>?            ScanError;

    public bool IsScanning    => _isScanning;
    public int  ScanIntervalMs { get; set; } = 2000;

    private WifiManager EnsureWifiManager()
    {
        if (_wifiManager is null)
        {
            var ctx = AndroidApp.Context;
            _wifiManager = (WifiManager)ctx.GetSystemService(Context.WifiService)!;
        }
        return _wifiManager;
    }

    public async Task StartScanningAsync(CancellationToken cancellationToken = default)
    {
        if (_isScanning) return;
        _isScanning = true;

        var wm  = EnsureWifiManager();
        var ctx = AndroidApp.Context;

        // Register broadcast receiver for scan results
        _receiver = new ScanReceiver(OnScanResultsAvailable);
        var filter = new IntentFilter(WifiManager.ScanResultsAvailableAction);
#pragma warning disable CA1416 // context.RegisterReceiver requires API 33+ for exported flag on newer SDKs
        ctx.RegisterReceiver(_receiver, filter);
#pragma warning restore CA1416
        _receiverRegistered = true;

        // Scan loop — trigger a new scan every interval
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // StartScan() must be called off the main thread on some devices
                await Task.Run(() => wm.StartScan(), cancellationToken);
                await Task.Delay(ScanIntervalMs, cancellationToken);
            }
        }
        catch (System.OperationCanceledException) { /* expected */ }
        catch (Exception ex)
        {
            ScanError?.Invoke(this, new ScanErrorEventArgs
            {
                ErrorMessage = "Android WiFi scan error",
                Exception    = ex
            });
        }
        finally
        {
            _isScanning = false;
            Unregister();
        }
    }

    public void StopScanning()
    {
        _isScanning = false;
        Unregister();
    }

    private void Unregister()
    {
        if (_receiverRegistered && _receiver is not null)
        {
            try { AndroidApp.Context.UnregisterReceiver(_receiver); }
            catch { /* already unregistered */ }
            _receiverRegistered = false;
        }
    }

    private void OnScanResultsAvailable()
    {
        var wm      = EnsureWifiManager();
        var results = wm.ScanResults;
        if (results is null) return;

        var aps = new List<AccessPoint>(results.Count);
        foreach (var r in results)
        {
            if (r?.Bssid is null) continue;
            var ap = ParseScanResult(r);
            aps.Add(ap);
        }

        AccessPointsDetected?.Invoke(this, new AccessPointsDetectedEventArgs { AccessPoints = aps });
    }

    /// <summary>
    /// Convert an Android ScanResult to our model.
    /// Security parsing mirrors Network.java from wigle-wifi / vistumbler-android.
    /// </summary>
    private static AccessPoint ParseScanResult(ScanResult r)
    {
        var caps = r.Capabilities ?? string.Empty;

        return new AccessPoint
        {
            Bssid          = r.Bssid ?? string.Empty,
            Ssid           = r.Ssid  ?? string.Empty,
            FrequencyMhz   = r.Frequency,
            Channel        = FrequencyToChannel(r.Frequency),
            Rssi           = r.Level,
            Signal         = RssiToPercent(r.Level),
            RadioType      = FrequencyToRadioType(r.Frequency),
            NetworkType    = NetworkType.Infrastructure,
            Authentication = ParseAuthentication(caps),
            Encryption     = ParseEncryption(caps),
            IsActive       = true
        };
    }

    // ── Frequency → Channel ──────────────────────────────────────────────────
    // Mirrors the channel tables in Network.java (wigle-wifi-wardriving)
    private static int FrequencyToChannel(int freqMhz) => freqMhz switch
    {
        // 2.4 GHz band: ch1=2412 … ch14=2484
        >= 2412 and <= 2472 => (freqMhz - 2412) / 5 + 1,
        2484                => 14,
        // 5 GHz band: channels = (freq - 5000) / 5
        >= 5170 and <= 5825 => (freqMhz - 5000) / 5,
        // 6 GHz band: ch1=5955, step 20 MHz
        >= 5955 and <= 7115 => (freqMhz - 5955) / 20 * 4 + 1,
        _                   => 0
    };

    private static string FrequencyToRadioType(int freqMhz) => freqMhz switch
    {
        < 3000  => "802.11 2.4 GHz",
        < 6000  => "802.11 5 GHz",
        _       => "802.11 6 GHz"
    };

    // ── RSSI → signal % ──────────────────────────────────────────────────────
    // -30 dBm = 100%, -90 dBm = 0% (same scale as original Vistumbler)
    private static int RssiToPercent(int rssi)
    {
        if (rssi <= -90) return 0;
        if (rssi >= -30) return 100;
        return (rssi + 90) * 100 / 60;
    }

    // ── Security parsing from capabilities string ────────────────────────────
    // Mirrors Network.java capability constants and parsing logic.
    private static AuthenticationType ParseAuthentication(string caps)
    {
        if (caps.Contains("SAE",        StringComparison.OrdinalIgnoreCase)) return AuthenticationType.WPA3_PSK;
        if (caps.Contains("WPA3",       StringComparison.OrdinalIgnoreCase)) return AuthenticationType.WPA3;
        if (caps.Contains("EAP_SUITE_B",StringComparison.OrdinalIgnoreCase)) return AuthenticationType.WPA3_Enterprise;
        if (caps.Contains("WPA2-EAP",   StringComparison.OrdinalIgnoreCase)) return AuthenticationType.WPA2_Enterprise;
        if (caps.Contains("WPA-EAP",    StringComparison.OrdinalIgnoreCase)) return AuthenticationType.WPA_Enterprise;
        if (caps.Contains("WPA2-PSK",   StringComparison.OrdinalIgnoreCase)) return AuthenticationType.WPA2_PSK;
        if (caps.Contains("[WPA2",      StringComparison.OrdinalIgnoreCase)) return AuthenticationType.WPA2;
        if (caps.Contains("[RSN",       StringComparison.OrdinalIgnoreCase)) return AuthenticationType.WPA2;
        if (caps.Contains("WPA-PSK",    StringComparison.OrdinalIgnoreCase)) return AuthenticationType.WPA_PSK;
        if (caps.Contains("[WPA-",      StringComparison.OrdinalIgnoreCase)) return AuthenticationType.WPA;
        if (caps.Contains("OWE",        StringComparison.OrdinalIgnoreCase)) return AuthenticationType.OWE;
        if (caps.Contains("[WEP",       StringComparison.OrdinalIgnoreCase)) return AuthenticationType.Shared;
        return AuthenticationType.Open;
    }

    private static EncryptionType ParseEncryption(string caps)
    {
        if (caps.Contains("GCMP-256", StringComparison.OrdinalIgnoreCase)) return EncryptionType.GCMP;
        if (caps.Contains("GCMP",     StringComparison.OrdinalIgnoreCase)) return EncryptionType.GCMP;
        if (caps.Contains("CCMP",     StringComparison.OrdinalIgnoreCase)) return EncryptionType.CCMP;
        if (caps.Contains("TKIP",     StringComparison.OrdinalIgnoreCase)) return EncryptionType.TKIP;
        if (caps.Contains("[WEP",     StringComparison.OrdinalIgnoreCase)) return EncryptionType.WEP;
        return EncryptionType.None;
    }

    // ── Adapter enumeration (Android has one logical WiFi adapter) ───────────
    public Task<List<WiFiAdapter>> GetAvailableAdaptersAsync() =>
        Task.FromResult(new List<WiFiAdapter>
        {
            new WiFiAdapter { Id = "wlan0", Name = "Wi-Fi" }
        });

    public void SetActiveAdapter(string adapterId) => _activeAdapterId = adapterId;

    public Task<string?> GetConnectedBssidAsync()
    {
        var wm   = EnsureWifiManager();
#pragma warning disable CA1416
        var info = wm.ConnectionInfo;
#pragma warning restore CA1416
        var bssid = info?.BSSID;
        return Task.FromResult(bssid == "02:00:00:00:00:00" ? null : bssid);
    }

    // ── Inner BroadcastReceiver ───────────────────────────────────────────────
    private sealed class ScanReceiver : BroadcastReceiver
    {
        private readonly Action _onResults;
        public ScanReceiver(Action onResults) => _onResults = onResults;
        public override void OnReceive(Context? context, Intent? intent) => _onResults();
    }
}
#endif
