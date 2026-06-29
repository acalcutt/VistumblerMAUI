using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace VistumblerMAUI.Services;

/// <summary>
/// Cross-platform GPS service backed by MAUI's Geolocation API.
/// On Android this uses LocationManager (GPS_PROVIDER + NETWORK_PROVIDER fallback),
/// the same underlying providers as vistumbler-android's GNSSListener.
/// </summary>
public class MauiGeolocationGpsService : IGpsService
{
    private CancellationTokenSource? _cts;
    private DateTime _lastUpdate = DateTime.MinValue;

    public event EventHandler<GpsDataReceivedEventArgs>? GpsDataReceived;
    public event EventHandler<GpsErrorEventArgs>?        GpsError;

    public GpsData? CurrentGpsData  { get; private set; }
    public bool     IsActive        => _cts is not null && !_cts.IsCancellationRequested;
    public double   SecondsSinceLastUpdate =>
        _lastUpdate == DateTime.MinValue ? double.MaxValue : (DateTime.UtcNow - _lastUpdate).TotalSeconds;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            // Check permission
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
            {
                GpsError?.Invoke(this, new GpsErrorEventArgs
                {
                    ErrorMessage = "Location permission denied"
                });
                return;
            }

            var request = new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(5));

            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var loc = await Geolocation.GetLocationAsync(request, _cts.Token);
                    if (loc is not null)
                    {
                        var gps = new GpsData
                        {
                            Latitude   = loc.Latitude,
                            Longitude  = loc.Longitude,
                            Altitude   = loc.Altitude,
                            SpeedKnots = loc.Speed.HasValue ? loc.Speed.Value / 0.514444 : null,
                            TrackAngle = loc.Course,
                            Timestamp  = loc.Timestamp.UtcDateTime,
                            Quality    = GpsQuality.GpsFix
                        };
                        CurrentGpsData = gps;
                        _lastUpdate    = DateTime.UtcNow;
                        GpsDataReceived?.Invoke(this, new GpsDataReceivedEventArgs { GpsData = gps });
                    }
                }
                catch (FeatureNotSupportedException)
                {
                    GpsError?.Invoke(this, new GpsErrorEventArgs { ErrorMessage = "GPS not supported on this device" });
                    break;
                }
                catch (PermissionException)
                {
                    GpsError?.Invoke(this, new GpsErrorEventArgs { ErrorMessage = "GPS permission denied" });
                    break;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    GpsError?.Invoke(this, new GpsErrorEventArgs
                    {
                        ErrorMessage = "GPS error",
                        Exception    = ex
                    });
                }

                await Task.Delay(2000, _cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Stop() => _cts?.Cancel();
}
