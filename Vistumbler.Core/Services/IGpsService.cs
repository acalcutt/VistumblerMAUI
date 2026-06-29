using Vistumbler.Core.Models;

namespace Vistumbler.Core.Services;

/// <summary>
/// Provides GPS/GNSS position fixes.
/// On Android/iOS: wraps MAUI Geolocation API.
/// On Windows (optional): can also fall back to NMEA over serial port.
/// </summary>
public interface IGpsService
{
    event EventHandler<GpsDataReceivedEventArgs>? GpsDataReceived;
    event EventHandler<GpsErrorEventArgs>? GpsError;

    GpsData? CurrentGpsData { get; }
    bool IsActive { get; }
    double SecondsSinceLastUpdate { get; }

    Task StartAsync(CancellationToken cancellationToken = default);
    void Stop();
}

public class GpsDataReceivedEventArgs : EventArgs
{
    public GpsData GpsData { get; set; } = null!;
}

public class GpsErrorEventArgs : EventArgs
{
    public string ErrorMessage { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
}
