using Microsoft.Extensions.DependencyInjection;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace VistumblerMAUI.Services;

/// <summary>
/// The single <see cref="IGpsService"/> registered in DI. Routes to either the Windows/OS
/// location service or a serial NMEA receiver based on <see cref="GpsSettings.Source"/>
/// (mirrors VistumblerCS's GpsServiceRouter). The serial back-end is optional and only
/// present on platforms that register <see cref="ISerialGpsService"/> (Windows).
/// </summary>
public class GpsRouterService : IGpsService
{
    private readonly MauiGeolocationGpsService _location;
    private readonly ISerialGpsService?        _serial;
    private IGpsService?                        _active;

    public event EventHandler<GpsDataReceivedEventArgs>? GpsDataReceived;
    public event EventHandler<GpsErrorEventArgs>?        GpsError;

    public GpsRouterService(MauiGeolocationGpsService location, IServiceProvider services)
    {
        _location = location;
        _serial   = services.GetService<ISerialGpsService>();   // null off Windows

        _location.GpsDataReceived += (_, e) => GpsDataReceived?.Invoke(this, e);
        _location.GpsError        += (_, e) => GpsError?.Invoke(this, e);
        if (_serial is not null)
        {
            _serial.GpsDataReceived += (_, e) => GpsDataReceived?.Invoke(this, e);
            _serial.GpsError        += (_, e) => GpsError?.Invoke(this, e);
        }
    }

    public GpsData? CurrentGpsData        => _active?.CurrentGpsData;
    public bool     IsActive              => _active?.IsActive ?? false;
    public double   SecondsSinceLastUpdate => _active?.SecondsSinceLastUpdate ?? double.MaxValue;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        Stop();

        if (GpsSettings.Source == GpsSource.SerialNmea)
        {
            if (_serial is null)
            {
                GpsError?.Invoke(this, new GpsErrorEventArgs
                {
                    ErrorMessage = "serial GPS is only available on Windows"
                });
                _active = _location;   // fall back so the user still gets a position
            }
            else
            {
                _active = _serial;
            }
        }
        else
        {
            _active = _location;
        }

        await _active.StartAsync(cancellationToken);
    }

    public void Stop()
    {
        _active?.Stop();
        _active = null;
    }
}
