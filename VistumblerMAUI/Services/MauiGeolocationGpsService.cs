using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace VistumblerMAUI.Services;

/// <summary>
/// Cross-platform GPS service backed by MAUI's Geolocation API using the <b>continuous</b>
/// foreground listener (Geolocation.StartListeningForegroundAsync + LocationChanged) — the
/// same push-based mechanism the map's MyLocation uses. This is far more reliable than
/// polling GetLocationAsync (which frequently returns null on unpackaged Windows), while
/// still working on Android/iOS. A serial/NMEA source (like VistumblerCS's COM-port option)
/// can be added as a second IGpsService and selected via settings.
/// </summary>
public class MauiGeolocationGpsService : IGpsService
{
    private bool _listening;
    private DateTime _lastUpdate = DateTime.MinValue;

    public event EventHandler<GpsDataReceivedEventArgs>? GpsDataReceived;
    public event EventHandler<GpsErrorEventArgs>?        GpsError;

    public GpsData? CurrentGpsData  { get; private set; }
    public bool     IsActive        => _listening;
    public double   SecondsSinceLastUpdate =>
        _lastUpdate == DateTime.MinValue ? double.MaxValue : (DateTime.UtcNow - _lastUpdate).TotalSeconds;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        // Permission
        var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted)
            status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted)
        {
            GpsError?.Invoke(this, new GpsErrorEventArgs { ErrorMessage = "permission denied" });
            return;
        }

        // Show a cached fix immediately if one is available.
        try
        {
            var last = await Geolocation.GetLastKnownLocationAsync();
            if (last is not null) Publish(last);
        }
        catch { /* fall through to live updates */ }

        // Continuous foreground updates (push-based), matching the map's location provider.
        try
        {
            if (!Geolocation.IsListeningForeground)
            {
                Geolocation.LocationChanged  += OnLocationChanged;
                Geolocation.ListeningFailed  += OnListeningFailed;

                var request = new GeolocationListeningRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(1));
                _listening = await Geolocation.StartListeningForegroundAsync(request);

                if (!_listening)
                    GpsError?.Invoke(this, new GpsErrorEventArgs
                    {
                        ErrorMessage = "could not start location updates (is Windows Location on?)"
                    });
            }
            else
            {
                _listening = true;
            }
        }
        catch (FeatureNotSupportedException)
        {
            GpsError?.Invoke(this, new GpsErrorEventArgs { ErrorMessage = "not supported on this device" });
        }
        catch (PermissionException)
        {
            GpsError?.Invoke(this, new GpsErrorEventArgs { ErrorMessage = "permission denied" });
        }
        catch (Exception ex)
        {
            GpsError?.Invoke(this, new GpsErrorEventArgs { ErrorMessage = $"unavailable ({ex.Message})", Exception = ex });
        }
    }

    public void Stop()
    {
        try { if (_listening) Geolocation.StopListeningForeground(); }
        catch { /* ignore */ }
        Geolocation.LocationChanged -= OnLocationChanged;
        Geolocation.ListeningFailed -= OnListeningFailed;
        _listening = false;
    }

    private void OnLocationChanged(object? sender, GeolocationLocationChangedEventArgs e)
        => Publish(e.Location);

    private void OnListeningFailed(object? sender, GeolocationListeningFailedEventArgs e)
    {
        _listening = false;
        GpsError?.Invoke(this, new GpsErrorEventArgs { ErrorMessage = $"updates stopped ({e.Error})" });
    }

    private void Publish(Location loc)
    {
        var gps = new GpsData
        {
            Latitude   = loc.Latitude,
            Longitude  = loc.Longitude,
            Altitude   = loc.Altitude,
            SpeedKnots = loc.Speed.HasValue ? loc.Speed.Value / 0.514444 : null,
            TrackAngle = loc.Course,
            Accuracy   = loc.Accuracy,
            Timestamp  = loc.Timestamp.UtcDateTime,
            Quality    = GpsQuality.GpsFix
        };
        CurrentGpsData = gps;
        _lastUpdate    = DateTime.UtcNow;
        GpsDataReceived?.Invoke(this, new GpsDataReceivedEventArgs { GpsData = gps });
    }
}
