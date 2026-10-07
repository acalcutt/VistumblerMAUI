using Microsoft.Extensions.DependencyInjection;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;

namespace VistumblerMAUI.Services;

/// <summary>
/// The single <see cref="IGpsService"/> registered in DI. Routes to the OS location service or an external
/// NMEA receiver based on <see cref="GpsSettings.Source"/> (mirrors VistumblerCS's GpsServiceRouter). The
/// external receivers are optional and only present where a platform registers them: a serial COM port
/// (<see cref="ISerialGpsService"/>, Windows), or a Bluetooth (<see cref="IBluetoothGpsService"/>) or USB
/// (<see cref="IUsbGpsService"/>) receiver on Android.
/// </summary>
public class GpsRouterService : IGpsService
{
    private readonly ILocationGpsService   _location;
    private readonly ISerialGpsService?    _serial;
    private readonly IBluetoothGpsService? _bluetooth;
    private readonly IUsbGpsService?       _usb;
    private IGpsService?                   _active;

    public event EventHandler<GpsDataReceivedEventArgs>? GpsDataReceived;
    public event EventHandler<GpsErrorEventArgs>?        GpsError;

    public GpsRouterService(ILocationGpsService location, IServiceProvider services)
    {
        _location  = location;
        _serial    = services.GetService<ISerialGpsService>();      // Windows only
        _bluetooth = services.GetService<IBluetoothGpsService>();   // Android only
        _usb       = services.GetService<IUsbGpsService>();         // Android only

        foreach (var source in new IGpsService?[] { _location, _serial, _bluetooth, _usb })
        {
            if (source is null) continue;
            source.GpsDataReceived += (_, e) => GpsDataReceived?.Invoke(this, e);
            source.GpsError        += (_, e) => GpsError?.Invoke(this, e);
        }
    }

    public GpsData? CurrentGpsData        => _active?.CurrentGpsData;
    public bool     IsActive              => _active?.IsActive ?? false;
    public double   SecondsSinceLastUpdate => _active?.SecondsSinceLastUpdate ?? double.MaxValue;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        Stop();

        // GpsSettings.Source only returns sources this platform offers, so a missing back-end is unexpected;
        // fall back to the device so the user still gets a position
        _active = GpsSettings.Source switch
        {
            GpsSource.SerialNmea    => (IGpsService?)_serial,
            GpsSource.BluetoothNmea => _bluetooth,
            GpsSource.UsbNmea       => _usb,
            _                       => _location,
        } ?? _location;

        await _active.StartAsync(cancellationToken);
    }

    public void Stop()
    {
        _active?.Stop();
        _active = null;
    }
}
