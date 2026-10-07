#if ANDROID
using System.Text;
using Android.App;
using Android.Content;
using Android.Hardware.Usb;
using Anotherlab.UsbSerialForAndroid.Driver;
using Vistumbler.Core.Models;
using Vistumbler.Core.Services;
using VistumblerMAUI.Services;
using AndroidApp = Android.App.Application;

namespace VistumblerMAUI.Platforms.Android;

/// <summary>
/// GPS from a USB receiver plugged into the phone (through an OTG adapter). UsbSerialForAndroid drives the
/// receiver's USB serial chip (PL2303, CP210x, FTDI, CH34x, CDC-ACM) through Android's USB host API, so no driver
/// install is needed; the NMEA it sends is parsed with <see cref="NmeaParser"/>, like the Bluetooth and COM-port
/// sources. Android asks the user to allow the app to use the device the first time. Reconnects every few
/// seconds while GPS is on, so unplugging and replugging the receiver picks it up again, and reconnects a
/// receiver that goes silent when <see cref="GpsSettings.ReconnectWhenNoData"/> is on.
/// </summary>
public class UsbNmeaGpsService : IUsbGpsService
{
    private const string PermissionAction = "com.vistumbler.mauiapp.USB_GPS_PERMISSION";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private NmeaParser _parser = new();
    private UsbSerialPort? _port;
    private CancellationTokenSource? _cts;
    private volatile bool _connected;
    private DateTime _lastUpdate = DateTime.MinValue;

    public event EventHandler<GpsDataReceivedEventArgs>? GpsDataReceived;
    public event EventHandler<GpsErrorEventArgs>?        GpsError;

    public GpsData? CurrentGpsData => _parser.Current;
    public bool     IsActive       => _connected;
    public double   SecondsSinceLastUpdate =>
        _lastUpdate == DateTime.MinValue ? double.MaxValue : (DateTime.UtcNow - _lastUpdate).TotalSeconds;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        Stop();
        if (UsbManager is null)
        {
            Error("this device doesn't support USB host (OTG)");
            return Task.CompletedTask;
        }

        _parser = new NmeaParser();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cts.Token;
        _ = Task.Run(() => ReadLoopAsync(token), token);
        return Task.CompletedTask;
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        ClosePort();   // ends a pending read
        _connected = false;
    }

    public IReadOnlyList<string> GetAttachedDevices() =>
        UsbManager is { } manager
            ? UsbSerialProber.GetDefaultProber().FindAllDrivers(manager).Select(d => Describe(d.Device)).ToList()
            : Array.Empty<string>();

    private static UsbManager? UsbManager => AndroidApp.Context.GetSystemService(Context.UsbService) as UsbManager;

    private async Task ReadLoopAsync(CancellationToken token)
    {
        string? lastProblem = null;
        while (!token.IsCancellationRequested)
        {
            string? problem = null;
            try
            {
                problem = await ConnectAndReadAsync(token);
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                problem = $"USB GPS error ({ex.Message}); retrying";
            }
            catch
            {
                break;   // stopped
            }
            finally
            {
                _connected = false;
                ClosePort();
            }

            // Report a problem once rather than every retry, e.g. while nothing is plugged in
            if (problem is not null && problem != lastProblem) Error(problem);
            lastProblem = problem;

            try { await Task.Delay(RetryDelay, token); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Connects to the first supported USB serial device and reads it until it goes away; returns why it stopped.</summary>
    private async Task<string?> ConnectAndReadAsync(CancellationToken token)
    {
        var manager = UsbManager!;
        var driver = UsbSerialProber.GetDefaultProber().FindAllDrivers(manager).FirstOrDefault();
        if (driver is null) return "no USB GPS receiver plugged in; waiting for one";
        var device = driver.Device;
        var name = Describe(device);

        if (!manager.HasPermission(device) && !await RequestPermissionAsync(manager, device))
            return $"permission to use {name} was denied; unplug and replug it to be asked again";

        var connection = manager.OpenDevice(device);
        if (connection is null) return $"can't open {name}";

        var port = driver.Ports[0];
        port.Open(connection);
        _port = port;
        port.SetParameters(GpsSettings.UsbBaudRate, UsbSerialPort.DATABITS_8, StopBits.One, Parity.None);
        try { port.SetDTR(true); port.SetRTS(true); } catch { /* not every chip has the lines */ }
        _connected = true;
        DebugLog.Write($"[GpsSvc-USB] connected to {name} at {GpsSettings.UsbBaudRate} baud");

        // Bytes arrive in arbitrary chunks; split them into NMEA lines
        var buffer = new byte[4096];
        var line = new StringBuilder();
        var lastData = DateTime.UtcNow;
        while (!token.IsCancellationRequested)
        {
            int read = port.Read(buffer, 1000);   // 0 on timeout
            if (read > 0)
                lastData = DateTime.UtcNow;
            else if (GpsSettings.ReconnectWhenNoData && DateTime.UtcNow - lastData > GpsSettings.NoDataTimeout)
                return $"no data from {name} for {GpsSettings.NoDataTimeout.TotalSeconds:0} seconds; reconnecting";
            for (int i = 0; i < read; i++)
            {
                char c = (char)buffer[i];
                if (c == '\n')
                {
                    if (_parser.Process(line.ToString()) is { } fix)
                    {
                        _lastUpdate = DateTime.UtcNow;
                        GpsDataReceived?.Invoke(this, new GpsDataReceivedEventArgs { GpsData = fix });
                    }
                    line.Clear();
                }
                else if (c != '\r' && line.Length < 512)
                {
                    line.Append(c);
                }
            }
        }
        return null;
    }

    private static Task<bool> RequestPermissionAsync(UsbManager manager, UsbDevice device)
    {
        var ctx = AndroidApp.Context;
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receiver = new PermissionReceiver(done);
        var filter = new IntentFilter(PermissionAction);
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            ctx.RegisterReceiver(receiver, filter, ReceiverFlags.NotExported);
        else
            ctx.RegisterReceiver(receiver, filter);

        // The system fills in the result extras, so the intent must be mutable; explicit so only this app receives it
        var intent = new Intent(PermissionAction).SetPackage(ctx.PackageName);
        var flags = OperatingSystem.IsAndroidVersionAtLeast(31) ? PendingIntentFlags.Mutable : 0;
        manager.RequestPermission(device, PendingIntent.GetBroadcast(ctx, 0, intent, flags));
        return done.Task;
    }

    private sealed class PermissionReceiver(TaskCompletionSource<bool> done) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            try { context?.UnregisterReceiver(this); } catch { /* already */ }
            done.TrySetResult(intent?.GetBooleanExtra(UsbManager.ExtraPermissionGranted, false) ?? false);
        }
    }

    private void ClosePort()
    {
        var port = _port;
        _port = null;
        try { port?.Close(); } catch { /* already closed or unplugged */ }
    }

    private void Error(string message) =>
        GpsError?.Invoke(this, new GpsErrorEventArgs { ErrorMessage = message });

    private static string Describe(UsbDevice device)
    {
        var name = string.Join(" ", new[] { device.ManufacturerName, device.ProductName }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        var ids = $"{device.VendorId:X4}:{device.ProductId:X4}";
        return string.IsNullOrWhiteSpace(name) ? $"USB device {ids}" : $"{name} ({ids})";
    }
}
#endif
