#if IOS
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace VistumblerMAUI.Platforms.iOS;

/// <summary>
/// iOS does not allow apps to enumerate nearby WiFi networks.
/// NEHotspotNetwork can only return the currently connected SSID/BSSID.
/// This stub surfaces that single AP so scanning "works" in a limited sense.
/// </summary>
public class iOSWiFiScannerService : IWiFiScannerService
{
    public event EventHandler<AccessPointsDetectedEventArgs>? AccessPointsDetected;
    public event EventHandler<ScanErrorEventArgs>?            ScanError;

    public bool IsScanning     { get; private set; }
    public int  ScanIntervalMs { get; set; } = 5000;

    public async Task StartScanningAsync(CancellationToken cancellationToken = default)
    {
        IsScanning = true;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var bssid = await GetConnectedBssidAsync();
                if (bssid is not null)
                {
                    AccessPointsDetected?.Invoke(this, new AccessPointsDetectedEventArgs
                    {
                        AccessPoints = new List<AccessPoint>
                        {
                            new AccessPoint
                            {
                                Bssid    = bssid,
                                Ssid     = "(current network)",
                                IsActive = true
                            }
                        }
                    });
                }
                await Task.Delay(ScanIntervalMs, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
        finally { IsScanning = false; }
    }

    public void StopScanning() => IsScanning = false;

    public Task<List<WiFiAdapter>> GetAvailableAdaptersAsync() =>
        Task.FromResult(new List<WiFiAdapter>
        {
            new WiFiAdapter { Id = "wifi0", Name = "Wi-Fi" }
        });

    public void SetActiveAdapter(string adapterId) { }

    public async Task<string?> GetConnectedBssidAsync()
    {
        // NEHotspotNetwork.FetchCurrent requires the HotspotHelper entitlement
        // (carrier-restricted). Without it we get null. Return null for now.
        return await Task.FromResult<string?>(null);
    }
}
#endif
