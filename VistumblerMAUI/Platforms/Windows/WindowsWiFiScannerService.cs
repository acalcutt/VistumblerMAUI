#if WINDOWS
using ManagedNativeWifi;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;
// ManagedNativeWifi also defines an EncryptionType; alias the ambiguous name to ours.
using EncryptionType = Vistumbler.Core.Models.EncryptionType;

namespace VistumblerMAUI.Platforms.Windows;

/// <summary>
/// Windows WiFi scanner using ManagedNativeWifi (WLAN API).
/// Directly mirrors the VistumblerCS NativeWiFiScanner implementation.
/// </summary>
public class WindowsWiFiScannerService : IWiFiScannerService
{
    private bool  _isScanning;
    // The adapters chosen in Settings; empty, or none of them plugged in, scans with them all
    private volatile IReadOnlyCollection<Guid> _chosenAdapters = Array.Empty<Guid>();

    public event EventHandler<AccessPointsDetectedEventArgs>? AccessPointsDetected;
    public event EventHandler<ScanErrorEventArgs>?            ScanError;

    public bool IsScanning     => _isScanning;
    public int  ScanIntervalMs { get; set; } = 1000;

    public async Task StartScanningAsync(CancellationToken cancellationToken = default)
    {
        if (_isScanning) return;
        _isScanning = true;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await ScanOnceAsync();
                await Task.Delay(ScanIntervalMs, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ScanError?.Invoke(this, new ScanErrorEventArgs
            {
                ErrorMessage = "Windows WiFi scan error",
                Exception    = ex
            });
        }
        finally
        {
            _isScanning = false;
        }
    }

    private async Task ScanOnceAsync()
    {
        try
        {
            // The chosen adapters that are plugged in, or all of them. v3.0.2 API: ScanNetworksAsync(TimeSpan) scans
            // all interfaces; the per-interface overload takes ScanMode + IEnumerable<Guid>.
            var plugged = NativeWifi.EnumerateInterfaces().Select(i => i.Id).ToHashSet();
            var active = _chosenAdapters.Where(plugged.Contains).ToHashSet();
            if (active.Count == 0) active = plugged;
            if (active.Count < plugged.Count)
                await NativeWifi.ScanNetworksAsync(ScanMode.OnlySpecified, active, TimeSpan.FromSeconds(3), CancellationToken.None);
            else
                await NativeWifi.ScanNetworksAsync(TimeSpan.FromSeconds(3));

            var aps = new List<AccessPoint>();

            // EnumerateBssNetworks() carries no security info, so build a SSID → security
            // lookup from EnumerateAvailableNetworks() (which exposes the WLAN
            // AuthenticationAlgorithm / CipherAlgorithm) and translate those native flags
            // into Vistumbler's enums — the same approach as VistumblerCS's NativeWiFiScanner.
            var security = NativeWifi.EnumerateAvailableNetworks()
                .Where(n => active.Contains(n.InterfaceInfo.Id))
                .GroupBy(n => n.Ssid.ToString())
                .ToDictionary(grp => grp.Key, grp => grp.First(), StringComparer.Ordinal);

            // EnumerateBssNetworks() returns per-BSSID detail in v3.0.2, from every adapter: only the chosen ones'
            // are kept, and an AP two of them heard is listed once, as the stronger reading
            var heard = NativeWifi.EnumerateBssNetworks()
                .Where(b => active.Contains(b.InterfaceInfo.Id))
                .GroupBy(b => b.Bssid.ToString(), StringComparer.OrdinalIgnoreCase)
                .Select(grp => grp.MaxBy(b => b.Rssi)!);
            foreach (var bss in heard)
            {
                var ssid = bss.Ssid.ToString();
                var ap = new AccessPoint
                {
                    Bssid        = bss.Bssid.ToString(),          // NetworkIdentifier.ToString() → XX:XX:XX:XX:XX:XX
                    Ssid         = ssid,
                    FrequencyMhz = bss.Frequency / 1000,          // kHz → MHz
                    Channel      = bss.Channel,                   // direct property in v3.0.2
                    Rssi         = bss.Rssi,
                    Signal       = bss.LinkQuality,
                    RadioType    = bss.PhyType.ToString(),
                    NetworkType  = NetworkType.Infrastructure,
                    Authentication = AuthenticationType.Unknown,
                    Encryption     = EncryptionType.Unknown,
                    IsActive     = true
                };

                // Security is per-SSID here (EnumerateBssNetworks has none per BSSID).
                if (security.TryGetValue(ssid, out var net))
                {
                    ap.Authentication = MapAuthentication(net.AuthenticationAlgorithm);
                    ap.Encryption     = MapEncryption(net.CipherAlgorithm);
                }

                aps.Add(ap);
            }

            if (aps.Count > 0)
                AccessPointsDetected?.Invoke(this, new AccessPointsDetectedEventArgs { AccessPoints = aps });
        }
        catch (Exception ex)
        {
            ScanError?.Invoke(this, new ScanErrorEventArgs
            {
                ErrorMessage = "Scan enumeration error",
                Exception    = ex
            });
        }
    }

    public void StopScanning() => _isScanning = false;

    public Task<List<WiFiAdapter>> GetAvailableAdaptersAsync()
    {
        var adapters = NativeWifi.EnumerateInterfaces()
            .Select(i => new WiFiAdapter { Id = i.Id.ToString(), Name = i.Description })
            .ToList();
        return Task.FromResult(adapters);
    }

    public void SetActiveAdapters(IReadOnlyCollection<string> adapterIds) =>
        _chosenAdapters = adapterIds.Select(id => Guid.TryParse(id, out var g) ? g : (Guid?)null).OfType<Guid>().ToArray();

    public async Task<string?> GetConnectedBssidAsync()
    {
        // EnumerateConnectedNetworkSsids returns SSIDs (not BSSIDs).
        // Cross-reference with EnumerateBssNetworks to find the connected BSSID.
        var connectedSsids = NativeWifi.EnumerateConnectedNetworkSsids()
            .Select(s => s.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var match = NativeWifi.EnumerateBssNetworks()
            .FirstOrDefault(b => connectedSsids.Contains(b.Ssid.ToString()));

        return await Task.FromResult(match?.Bssid.ToString());
    }

    // Translate the WLAN native authentication algorithm to Vistumbler's enum.
    // Mirrors VistumblerCS's NativeWiFiScanner.MapAuthentication (ManagedNativeWifi 3.0.2).
    private static AuthenticationType MapAuthentication(AuthenticationAlgorithm algo) => algo switch
    {
        AuthenticationAlgorithm.Open         => AuthenticationType.Open,
        AuthenticationAlgorithm.Shared       => AuthenticationType.Shared,
        AuthenticationAlgorithm.WPA          => AuthenticationType.WPA,
        AuthenticationAlgorithm.WPA_PSK      => AuthenticationType.WPA_PSK,
        AuthenticationAlgorithm.WPA_NONE     => AuthenticationType.WPA_None,
        AuthenticationAlgorithm.RSNA         => AuthenticationType.WPA2,
        AuthenticationAlgorithm.RSNA_PSK     => AuthenticationType.WPA2_PSK,
        AuthenticationAlgorithm.WPA3_ENT_192 => AuthenticationType.WPA3_Enterprise_192,
        AuthenticationAlgorithm.WPA3_ENT     => AuthenticationType.WPA3_Enterprise,
        AuthenticationAlgorithm.WPA3_SAE     => AuthenticationType.WPA3_PSK,
        AuthenticationAlgorithm.OWE          => AuthenticationType.OWE,
        _ => AuthenticationType.Unknown
    };

    // Translate the WLAN native cipher algorithm to Vistumbler's enum.
    // Mirrors VistumblerCS's NativeWiFiScanner.MapEncryption (ManagedNativeWifi 3.0.2).
    private static EncryptionType MapEncryption(CipherAlgorithm cipher) => cipher switch
    {
        CipherAlgorithm.None          => EncryptionType.None,
        CipherAlgorithm.WEP           => EncryptionType.WEP,
        CipherAlgorithm.WEP_40        => EncryptionType.WEP,
        CipherAlgorithm.WEP_104       => EncryptionType.WEP,
        CipherAlgorithm.TKIP          => EncryptionType.TKIP,
        CipherAlgorithm.CCMP          => EncryptionType.CCMP,
        CipherAlgorithm.CCMP_256      => EncryptionType.CCMP_256,
        CipherAlgorithm.BIP           => EncryptionType.BIP,
        CipherAlgorithm.GCMP          => EncryptionType.GCMP,
        CipherAlgorithm.GCMP_256      => EncryptionType.GCMP_256,
        CipherAlgorithm.BIP_GMAC_128  => EncryptionType.BIP_GMAC_128,
        CipherAlgorithm.BIP_GMAC_256  => EncryptionType.BIP_GMAC_256,
        CipherAlgorithm.BIP_CMAC_256  => EncryptionType.BIP_CMAC_256,
        _ => EncryptionType.Unknown
    };

    private static int FrequencyToChannel(int freqMhz) => freqMhz switch
    {
        >= 2412 and <= 2472 => (freqMhz - 2412) / 5 + 1,
        2484                => 14,
        >= 5170 and <= 5825 => (freqMhz - 5000) / 5,
        >= 5955 and <= 7115 => (freqMhz - 5955) / 20 * 4 + 1,
        _                   => 0
    };
}
#endif
