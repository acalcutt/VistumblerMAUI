#if ANDROID
using System.Text;
using Android.Bluetooth;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;
using VistumblerMAUI.Services;
using AndroidApp = Android.App.Application;
using Context = Android.Content.Context;

namespace VistumblerMAUI.Platforms.Android;

/// <summary>
/// GPS from an external Bluetooth receiver: connects to the paired device chosen in Settings → GPS over the
/// classic serial port profile (SPP), reads its NMEA sentences and parses them with <see cref="NmeaParser"/>,
/// like the Windows COM-port source. Reconnects every few seconds while GPS is on, so a receiver that is
/// switched off or drops out comes back by itself, as does one that goes silent when
/// <see cref="GpsSettings.ReconnectWhenNoData"/> is on.
/// </summary>
public class BluetoothNmeaGpsService : IBluetoothGpsService
{
    // The serial port profile, which Bluetooth GPS receivers use to stream NMEA
    private static readonly Java.Util.UUID SppUuid = Java.Util.UUID.FromString("00001101-0000-1000-8000-00805F9B34FB")!;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private NmeaParser _parser = new();
    private BluetoothSocket? _socket;
    private CancellationTokenSource? _cts;
    private volatile bool _connected;
    private DateTime _lastUpdate = DateTime.MinValue;

    public event EventHandler<GpsDataReceivedEventArgs>? GpsDataReceived;
    public event EventHandler<GpsErrorEventArgs>?        GpsError;

    public GpsData? CurrentGpsData => _parser.Current;
    public bool     IsActive       => _connected;
    public double   SecondsSinceLastUpdate =>
        _lastUpdate == DateTime.MinValue ? double.MaxValue : (DateTime.UtcNow - _lastUpdate).TotalSeconds;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        Stop();

        var address = GpsSettings.BluetoothAddress;
        if (string.IsNullOrWhiteSpace(address))
        {
            Error("no Bluetooth GPS receiver chosen in Settings → GPS");
            return;
        }
        if (!await EnsurePermissionAsync())
        {
            Error("Bluetooth permission denied");
            return;
        }
        var adapter = GetAdapter();
        if (adapter is null)
        {
            Error("this device has no Bluetooth");
            return;
        }
        if (!adapter.IsEnabled)
        {
            Error("Bluetooth is off");
            return;
        }

        _parser = new NmeaParser();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cts.Token;
        _ = Task.Run(() => ReadLoopAsync(adapter, address, token), token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        CloseSocket();   // unblocks a pending connect or read
        _connected = false;
    }

    public async Task<IReadOnlyList<BluetoothGpsDevice>> GetPairedDevicesAsync()
    {
        if (!await EnsurePermissionAsync())
            throw new InvalidOperationException("Bluetooth permission was denied.");
        var adapter = GetAdapter() ?? throw new InvalidOperationException("This device has no Bluetooth.");
        if (!adapter.IsEnabled)
            throw new InvalidOperationException("Bluetooth is off. Turn it on, then refresh.");

        return (adapter.BondedDevices ?? Array.Empty<BluetoothDevice>())
            .Where(d => !string.IsNullOrEmpty(d.Address))
            .Select(d => new BluetoothGpsDevice(string.IsNullOrWhiteSpace(d.Name) ? d.Address! : d.Name!, d.Address!))
            .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private async Task ReadLoopAsync(BluetoothAdapter adapter, string address, CancellationToken token)
    {
        var name = string.IsNullOrWhiteSpace(GpsSettings.BluetoothName) ? address : GpsSettings.BluetoothName;
        var stalledMessage = $"no data from {name} for {GpsSettings.NoDataTimeout.TotalSeconds:0} seconds; reconnecting";
        while (!token.IsCancellationRequested)
        {
            bool stalled = false;
            using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(token);
            try
            {
                var device = adapter.GetRemoteDevice(address)!;
                adapter.CancelDiscovery();   // discovery slows a connection down a lot
                _socket = Connect(device);
                _connected = true;
                DebugLog.Write($"[GpsSvc-Bluetooth] connected to {name}");

                // A read blocks until a line arrives, so a receiver that goes silent is caught by closing the
                // socket from here, which ends the read; the loop then reconnects
                long lastData = DateTime.UtcNow.Ticks;
                _ = Task.Run(async () =>
                {
                    while (!watchdog.IsCancellationRequested)
                    {
                        await Task.Delay(1000, watchdog.Token);
                        if (GpsSettings.ReconnectWhenNoData &&
                            DateTime.UtcNow - new DateTime(Interlocked.Read(ref lastData)) > GpsSettings.NoDataTimeout)
                        {
                            stalled = true;
                            CloseSocket();
                            return;
                        }
                    }
                }, watchdog.Token);

                using var reader = new StreamReader(_socket.InputStream!, Encoding.ASCII);
                while (!token.IsCancellationRequested && await reader.ReadLineAsync(token) is { } line)
                {
                    Interlocked.Exchange(ref lastData, DateTime.UtcNow.Ticks);
                    if (_parser.Process(line) is not { } fix) continue;
                    _lastUpdate = DateTime.UtcNow;
                    GpsDataReceived?.Invoke(this, new GpsDataReceivedEventArgs { GpsData = fix });
                }
                if (!token.IsCancellationRequested) Error(stalled ? stalledMessage : $"{name} disconnected; reconnecting");
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                if (stalled) Error(stalledMessage);
                else Error($"can't connect to {name} ({ex.Message}); retrying", ex);
            }
            catch
            {
                break;   // stopped
            }
            finally
            {
                watchdog.Cancel();
                _connected = false;
                CloseSocket();
            }

            try { await Task.Delay(RetryDelay, token); }
            catch (OperationCanceledException) { break; }
        }
    }

    // Some receivers only accept an unauthenticated (insecure) connection, so fall back to one
    private static BluetoothSocket Connect(BluetoothDevice device)
    {
        var socket = device.CreateRfcommSocketToServiceRecord(SppUuid)!;
        try
        {
            socket.Connect();
            return socket;
        }
        catch (Java.IO.IOException)
        {
            try { socket.Close(); } catch { }
            socket = device.CreateInsecureRfcommSocketToServiceRecord(SppUuid)!;
            socket.Connect();
            return socket;
        }
    }

    private void CloseSocket()
    {
        var socket = _socket;
        _socket = null;
        try { socket?.Close(); } catch { /* already closed */ }
    }

    private void Error(string message, Exception? ex = null) =>
        GpsError?.Invoke(this, new GpsErrorEventArgs { ErrorMessage = message, Exception = ex });

    private static BluetoothAdapter? GetAdapter() =>
        (AndroidApp.Context.GetSystemService(Context.BluetoothService) as BluetoothManager)?.Adapter;

    // Android 12+ asks for "Nearby devices" (BLUETOOTH_CONNECT) to use a paired device; older versions grant it at install
    private static async Task<bool> EnsurePermissionAsync()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(31)) return true;
        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var status = await Permissions.CheckStatusAsync<BluetoothConnectPermission>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<BluetoothConnectPermission>();
            return status == PermissionStatus.Granted;
        });
    }

    private sealed class BluetoothConnectPermission : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
            new[] { (global::Android.Manifest.Permission.BluetoothConnect, true) };
    }
}
#endif
