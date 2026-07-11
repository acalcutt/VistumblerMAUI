#if ANDROID
using Android.Locations;
using Android.OS;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;
using VistumblerMAUI.Services;
using AndroidApp = Android.App.Application;
using Context = Android.Content.Context;
using Location = Android.Locations.Location;

namespace VistumblerMAUI.Platforms.Android;

/// <summary>
/// Android GPS service driving LocationManager directly, subscribing to every live
/// provider (fused, gps, …) at 1 s / 0 m — the same multi-provider pattern WiGLE
/// WiFi Wardriving (and vistumbler-android) uses, so if any one provider starves
/// another keeps the fix stream alive.
///
/// Exists because MAUI's Geolocation.StartListeningForegroundAsync was observed on
/// device (Samsung S24, Android 16) delivering exactly one LocationChanged fix after
/// start and then going permanently silent — its legacy Criteria-picked single
/// provider starves, killing the scanner's position stamps and the map puck.
/// </summary>
public class AndroidGpsService : Java.Lang.Object, ILocationGpsService, ILocationListener
{
    private LocationManager? _lm;
    private bool     _listening;
    private DateTime _lastUpdate = DateTime.MinValue;

    public event EventHandler<GpsDataReceivedEventArgs>? GpsDataReceived;
    public event EventHandler<GpsErrorEventArgs>?        GpsError;

    public GpsData? CurrentGpsData { get; private set; }
    public bool     IsActive       => _listening;
    public double   SecondsSinceLastUpdate =>
        _lastUpdate == DateTime.MinValue ? double.MaxValue : (DateTime.UtcNow - _lastUpdate).TotalSeconds;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted)
            status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted)
        {
            GpsError?.Invoke(this, new GpsErrorEventArgs { ErrorMessage = "permission denied" });
            return;
        }

        _lm ??= (LocationManager?)AndroidApp.Context.GetSystemService(Context.LocationService);
        if (_lm is null)
        {
            GpsError?.Invoke(this, new GpsErrorEventArgs { ErrorMessage = "location service unavailable" });
            return;
        }

        try
        {
            // Show a cached fix immediately while waiting for the first live update.
            var last = GetBestLastKnown(_lm);
            if (last is not null) Publish(last);

            // WiGLE-style: subscribe to every live provider (fused, gps, …) rather than
            // betting on one — if any single provider starves (fused indoors quirks,
            // raw gps indoors), another keeps the stream alive. "passive" only echoes
            // other apps' requests, and "network" is skipped to match WiGLE's default.
            int subscribed = 0;
            foreach (var provider in _lm.AllProviders)
            {
                if (provider is "passive" or LocationManager.NetworkProvider) continue;
                try
                {
                    // 1 s / 0 m: every fix the provider produces, on the main looper.
                    _lm.RequestLocationUpdates(provider, 1000, 0f, this, Looper.MainLooper);
                    subscribed++;
                    DebugLog.Write($"[GpsSvc-Android] listening on '{provider}'");
                }
                catch (Exception ex)
                {
                    DebugLog.Write($"[GpsSvc-Android] '{provider}' failed: {ex.Message}");
                }
            }

            _listening = subscribed > 0;
            if (!_listening)
                GpsError?.Invoke(this, new GpsErrorEventArgs { ErrorMessage = "no usable location provider" });
        }
        catch (Exception ex)
        {
            GpsError?.Invoke(this, new GpsErrorEventArgs
            {
                ErrorMessage = $"unavailable ({ex.Message})",
                Exception    = ex
            });
        }
    }

    public void Stop()
    {
        try { _lm?.RemoveUpdates(this); } catch { /* ignore */ }
        _listening = false;
    }

    // ── ILocationListener ─────────────────────────────────────────────────────

    public void OnLocationChanged(Location location)
    {
        DebugLog.Write($"[GpsSvc-Android] fix {location.Latitude:F6},{location.Longitude:F6} acc={location.Accuracy:F1}");
        Publish(location);
    }

    public void OnProviderDisabled(string provider)
        => GpsError?.Invoke(this, new GpsErrorEventArgs { ErrorMessage = $"{provider} provider disabled" });

    public void OnProviderEnabled(string provider) { }

    public void OnStatusChanged(string? provider, Availability status, Bundle? extras) { }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Location? GetBestLastKnown(LocationManager lm)
    {
        Location? best = null;
        foreach (var provider in lm.AllProviders)
        {
            try
            {
                var loc = lm.GetLastKnownLocation(provider);
                if (loc is not null && (best is null || loc.Time > best.Time))
                    best = loc;
            }
            catch { /* provider not accessible — skip */ }
        }
        return best;
    }

    private void Publish(Location loc)
    {
        var gps = new GpsData
        {
            Latitude   = loc.Latitude,
            Longitude  = loc.Longitude,
            Altitude   = loc.HasAltitude ? loc.Altitude : null,
            SpeedKnots = loc.HasSpeed    ? loc.Speed / 0.514444 : null,   // m/s → knots
            TrackAngle = loc.HasBearing  ? loc.Bearing : null,
            Accuracy   = loc.HasAccuracy ? loc.Accuracy : null,
            Timestamp  = DateTimeOffset.FromUnixTimeMilliseconds(loc.Time).UtcDateTime,
            Quality    = GpsQuality.GpsFix,
        };
        CurrentGpsData = gps;
        _lastUpdate    = DateTime.UtcNow;
        GpsDataReceived?.Invoke(this, new GpsDataReceivedEventArgs { GpsData = gps });
    }
}
#endif
