#if WINDOWS
using ManagedNativeWifi;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace VistumblerMAUI.Platforms.Windows;

/// <summary>
/// Windows WiFi scanner using ManagedNativeWifi (WLAN API).
/// Directly mirrors the VistumblerCS NativeWiFiScanner implementation.
/// </summary>
public class WindowsWiFiScannerService : IWiFiScannerService
{
    private bool  _isScanning;
    private string? _activeAdapterId;

    public event EventHandler<AccessPointsDetectedEventArgs>? AccessPointsDetected;
    public event EventHandler<ScanErrorEventArgs>?            ScanError;

    public bool IsScanning     => _isScanning;
    public int  ScanIntervalMs { get; set; } = 2000;

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
            // Trigger a fresh scan. v3.0.2 API: ScanNetworksAsync(TimeSpan) scans all
            // interfaces; the per-interface overload takes ScanMode + IEnumerable<Guid>.
            if (_activeAdapterId is not null && Guid.TryParse(_activeAdapterId, out var ifaceGuid))
                await NativeWifi.ScanNetworksAsync(ScanMode.OnlySpecified, new[] { ifaceGuid }, TimeSpan.FromSeconds(3), CancellationToken.None);
            else
                await NativeWifi.ScanNetworksAsync(TimeSpan.FromSeconds(3));

            var aps = new List<AccessPoint>();

            // EnumerateBssNetworks() returns per-BSSID detail in v3.0.2
            foreach (var bss in NativeWifi.EnumerateBssNetworks())
            {
                var ap = new AccessPoint
                {
                    Bssid        = bss.Bssid.ToString(),          // NetworkIdentifier.ToString() → XX:XX:XX:XX:XX:XX
                    Ssid         = bss.Ssid.ToString(),
                    FrequencyMhz = bss.Frequency / 1000,          // kHz → MHz
                    Channel      = bss.Channel,                   // direct property in v3.0.2
                    Rssi         = bss.Rssi,
                    Signal       = bss.LinkQuality,
                    RadioType    = bss.PhyType.ToString(),
                    NetworkType  = NetworkType.Infrastructure,
                    IsActive     = true
                };
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

    public void SetActiveAdapter(string adapterId) => _activeAdapterId = adapterId;

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
